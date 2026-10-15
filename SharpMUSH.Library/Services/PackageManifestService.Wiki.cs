using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>wiki:</c> block and the <c>locale:</c> key (format 1.4).</summary>
public partial class PackageManifestService
{
	private static readonly IReadOnlySet<string> KnownWikiKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"page", "title", "categories", "file", "markdown", "requires", "published", "pin"
	};

	/// <summary>The namespaces a package page may be in. Biographies and engine pages are not package content.</summary>
	private static readonly IReadOnlySet<WikiNamespace> PackageWikiNamespaces = new HashSet<WikiNamespace>
	{
		WikiNamespace.Main, WikiNamespace.Help, WikiNamespace.Category
	};

	/// <summary>The longest slug, in UTF-8 bytes: it is part of a stored key, which has a limit of its own.</summary>
	private const int MaxPageSlugBytes = 200;

	/// <summary>The locale the pages are written in, canonical; null when the manifest names none.</summary>
	private static string? ReadLocale(Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		if (OptionalString(doc, "locale", issues) is not { } text)
		{
			return null;
		}

		switch (WikiHelpers.NormalizeLocale(text))
		{
			case string locale:
				return locale;
			case Error<string> error:
				issues.Add(PackageManifestIssue.Error("locale", error.Value));
				return null;
		}

		return null;
	}

	/// <summary>
	/// The wiki pages a package ships. Each is identified by its namespace and slug; the body is a file inside the
	/// package directory or inline Markdown, never both, and is literal text (no <c>{{ref}}</c> substitution).
	/// </summary>
	private static IReadOnlyList<PackageWikiPageSpec> ReadWiki(
		Dictionary<string, object?> doc, PackageKind kind, PackageDeclarations declared, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("wiki", out var node) || node is null)
		{
			return [];
		}

		if (kind is PackageKind.Plugin or PackageKind.Ui)
		{
			issues.Add(PackageManifestIssue.Error("wiki",
				"A plugin package cannot declare 'wiki'; ship the pages in a softcode package it depends on."));
			return [];
		}

		var declaredPermissions = declared.Permissions.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
		var result = new List<PackageWikiPageSpec>();
		var seen = new HashSet<(WikiNamespace, string)>();
		foreach (var (entry, path) in Entries(doc, "wiki", "wiki", KnownWikiKeys, issues))
		{
			if (ReadWikiPage(entry, path, declaredPermissions, issues) is not { } page)
			{
				continue;
			}

			if (!seen.Add((page.Namespace, page.Page)))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.page", $"Page {page.Label} is listed more than once."));
				continue;
			}

			result.Add(page);
		}

		// Inline bodies are here to check; a file: body is checked when it is read (PackageWikiBodies).
		foreach (var page in result.Where(page => page.Markdown is not null))
		{
			issues.AddRange(PackageWikiLinks.Check(page, result, $"wiki {page.Label}"));
		}

		return result;
	}

	private static PackageWikiPageSpec? ReadWikiPage(
		Dictionary<string, object?> entry, string path, IReadOnlySet<string> declaredPermissions, List<PackageManifestIssue> issues)
	{
		var errors = issues.Count(i => i.Severity == PackageManifestIssueSeverity.Error);

		if (entry.ContainsKey("namespace"))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.namespace",
				"'namespace' is part of 'page': write the page as namespace:slug, such as help:orc_license_notice."));
		}

		var name = ReadWikiPageName(entry, path, issues);

		var title = (entry.GetValueOrDefault("title") as string)?.Trim() ?? "";
		if (title.Length == 0 || title.Any(char.IsControl))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.title", "A page needs a title on one line."));
		}

		var categories = ReadWikiCategories(entry, path, issues);
		var (file, markdown) = ReadWikiBody(entry, path, issues);
		var published = ReadWikiFlag(entry, "published", path, true, issues);
		var pin = ReadWikiFlag(entry, "pin", path, false, issues);
		if (pin && name is { Namespace: not WikiNamespace.Category })
		{
			issues.Add(PackageManifestIssue.Error($"{path}.pin", "Only a category page can pin its category to the wiki home."));
		}

		var requires = ReadWikiRequires(entry, path, declaredPermissions, issues);

		if (issues.Count(i => i.Severity == PackageManifestIssueSeverity.Error) > errors || name is not { } page)
		{
			return null;
		}

		return new PackageWikiPageSpec(page.Slug, page.Namespace, title, categories, file, markdown, requires, published, pin);
	}

	/// <summary>
	/// The page's identity, <c>page: &lt;namespace&gt;:&lt;slug&gt;</c>: a namespace a package may ship (main, help or
	/// category, lower case) and a slug the wiki keeps as it is. Null, with the error, otherwise.
	/// </summary>
	private static (WikiNamespace Namespace, string Slug)? ReadWikiPageName(
		Dictionary<string, object?> entry, string path, List<PackageManifestIssue> issues)
	{
		var text = (entry.GetValueOrDefault("page") as string)?.Trim() ?? "";
		var colon = text.IndexOf(':');
		if (colon < 0)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.page",
				$"'{text}' does not name its namespace: write namespace:slug, such as main:{(text.Length == 0 ? "coast_road" : WikiHelpers.Slugify(text))}."));
			return null;
		}

		var prefix = text[..colon];
		var slug = text[(colon + 1)..];
		if (prefix is not ("main" or "help" or "category"))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.page",
				WikiHelpers.ParseNamespace(prefix) is { } ns && PackageWikiNamespaces.Contains(ns)
					? $"'{prefix}' is written in lower case: {WikiHelpers.NamespaceName(ns)}:{slug}."
					: WikiHelpers.ParseNamespace(prefix) is { } other
						? $"A package cannot ship {WikiHelpers.NamespaceName(other)} pages: biographies and engine pages are the game's. Use main, help or category."
						: $"'{prefix}' is not a namespace: main, help or category."));
			return null;
		}

		if (!IsPageSlug(slug))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.page",
				$"'{slug}' is not a page slug: lowercase, no spaces (use _), no ':' or '/', and one the wiki keeps as it is, such as {prefix}:coast_road."));
			return null;
		}

		return WikiHelpers.ParseNamespace(prefix) is { } parsed ? (parsed, slug) : null;
	}

	/// <summary>A slug the wiki keeps as it is, and one a URL and a key can carry.</summary>
	private static bool IsPageSlug(string page)
		=> page.Length > 0
			&& System.Text.Encoding.UTF8.GetByteCount(page) <= MaxPageSlugBytes
			&& WikiHelpers.Slugify(page) == page
			&& !page.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '/' or '\\' or '#' or '?');

	private static IReadOnlyList<string> ReadWikiCategories(Dictionary<string, object?> entry, string path, List<PackageManifestIssue> issues)
	{
		var names = entry.GetValueOrDefault("categories") switch
		{
			null => [],
			string single => [single],
			List<object> list => list.Select(item => item as string).ToList(),
			_ => (List<string?>?)null
		};

		if (names is null)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.categories", "'categories' must be a list of category names."));
			return [];
		}

		var result = new List<string>();
		for (var i = 0; i < names.Count; i++)
		{
			if (names[i]?.Trim() is not { Length: > 0 } name || name.Any(char.IsControl) || WikiHelpers.CategoryKey(name).Length == 0)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.categories[{i}]", "A category is a name on one line."));
				continue;
			}

			result.Add(name);
		}

		return result;
	}

	/// <summary>The body: exactly one of <c>file:</c> (a path inside the package directory) and <c>markdown:</c>.</summary>
	private static (string? File, string? Markdown) ReadWikiBody(Dictionary<string, object?> entry, string path, List<PackageManifestIssue> issues)
	{
		var fileNode = entry.GetValueOrDefault("file");
		var markdownNode = entry.GetValueOrDefault("markdown");
		if ((fileNode is null) == (markdownNode is null))
		{
			issues.Add(PackageManifestIssue.Error(path, "A page needs exactly one of 'file' and 'markdown'."));
			return (null, null);
		}

		if (markdownNode is not null)
		{
			if (markdownNode is not string markdown)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.markdown", "'markdown' must be text; use a block scalar (|)."));
				return (null, null);
			}

			return (null, PackageWikiContent.NormalizeMarkdown(markdown));
		}

		if (fileNode is not string file || PackageFilePath.Normalize(file) is not { } normalized)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.file",
				$"'{fileNode}' is not a file inside the package directory: a relative path with '/', no '..', such as wiki/coast-road.md."));
			return (null, null);
		}

		return (normalized, null);
	}

	private static bool ReadWikiFlag(Dictionary<string, object?> entry, string key, string path, bool fallback, List<PackageManifestIssue> issues)
	{
		if (entry.GetValueOrDefault(key) is not { } node)
		{
			return fallback;
		}

		if (bool.TryParse(node.ToString(), out var value))
		{
			return value;
		}

		issues.Add(PackageManifestIssue.Error($"{path}.{key}", $"'{key}' must be true or false."));
		return fallback;
	}

	/// <summary>
	/// What the page itself requires per action. A permission must be built in or a custom one: declared in the
	/// manifest's <c>permissions:</c>, or one the game must already have, which the plan checks.
	/// </summary>
	private static PackageWikiRequires ReadWikiRequires(
		Dictionary<string, object?> entry, string path, IReadOnlySet<string> declaredPermissions, List<PackageManifestIssue> issues)
	{
		if (entry.GetValueOrDefault("requires") is not { } node)
		{
			return PackageWikiRequires.None;
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.requires", "'requires' maps view, edit and delete to the permissions each needs."));
			return PackageWikiRequires.None;
		}

		var byAction = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (var (rawAction, rawScopes) in Normalize(map))
		{
			var action = rawAction.Trim().ToLowerInvariant();
			var actionPath = $"{path}.requires.{rawAction}";
			if (action is not ("view" or "edit" or "delete"))
			{
				issues.Add(PackageManifestIssue.Error(actionPath, $"'{rawAction}' is not an action: view, edit or delete."));
				continue;
			}

			var scopes = rawScopes switch
			{
				string single => [single],
				List<object> list => list.Select(item => item as string ?? "").ToList(),
				_ => new List<string>()
			};
			if (rawScopes is not (string or List<object>))
			{
				issues.Add(PackageManifestIssue.Error(actionPath, "Must be a list of permissions."));
				continue;
			}

			var valid = new List<string>();
			foreach (var scope in scopes.Select(s => s.Trim().ToLowerInvariant()))
			{
				if (PortalPermission.IsKnown(scope))
				{
					valid.Add(scope);
				}
				else if (CustomPermissions.IsValidName(scope))
				{
					valid.Add(scope);
					if (!declaredPermissions.Contains(scope))
					{
						issues.Add(PackageManifestIssue.Warning(actionPath,
							$"{scope} is not declared under permissions; the game must already define it."));
					}
				}
				else
				{
					issues.Add(PackageManifestIssue.Error(actionPath, $"'{scope}' is not a permission."));
				}
			}

			byAction[action] = valid;
		}

		return PackageWikiRequires.Of(byAction.GetValueOrDefault("view"), byAction.GetValueOrDefault("edit"),
			byAction.GetValueOrDefault("delete"));
	}
}
