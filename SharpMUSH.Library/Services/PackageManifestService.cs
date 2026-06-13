using System.Globalization;
using System.Text.RegularExpressions;
using OneOf;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Parses package.yaml manifests (format v2, decisions 20.11–20.20) and
/// index.yaml repo listings into validated models. Deserializes to a generic
/// node graph and hand-maps so every issue carries a precise document path,
/// and so shorthand forms (attribute value as a bare string, dependency as a
/// bare id) are supported.
/// </summary>
public partial class PackageManifestService : IPackageManifestService
{
	private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

	private static readonly IReadOnlySet<string> KnownManifestKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"format", "package", "version", "authors", "description", "license", "homepage", "keywords",
		"convention_prefix", "requires_server", "replaces", "conflicts", "depends", "configure", "objects"
	};

	private static readonly IReadOnlySet<string> ReservedManifestKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"provides", "recommends", "suggests"
	};

	private static readonly IReadOnlySet<string> KnownObjectKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"ref", "type", "name", "target", "parent", "location", "destination", "previous_refs", "flags", "locks", "attributes"
	};

	private static readonly IReadOnlySet<string> AttachForbiddenKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"type", "name", "parent", "location", "destination", "previous_refs", "flags", "locks"
	};

	private const int MaxPackageIdLength = 64;
	private const int MaxKeywords = 5;

	[GeneratedRegex("^[a-z][a-z0-9-]*$")]
	private static partial Regex PackageSlugRegex();

	[GeneratedRegex("^[a-z_][a-z0-9_]*$")]
	private static partial Regex RefNameRegex();

	[GeneratedRegex(@"^\S+$")]
	private static partial Regex AttributeNameRegex();

	private readonly IReadOnlySet<string> _wellKnownRefs;

	public PackageManifestService() : this([])
	{
	}

	/// <summary>
	/// Creates a parser whose well-known ref set is the built-in
	/// <see cref="WellKnownRefs.All"/> extended with server-configured names.
	/// </summary>
	public PackageManifestService(IEnumerable<string> additionalWellKnownRefs)
	{
		var set = new HashSet<string>(WellKnownRefs.All, StringComparer.Ordinal);
		set.UnionWith(additionalWellKnownRefs.Select(r => r.ToLowerInvariant()));
		_wellKnownRefs = set;
	}

	public OneOf<ParsedPackageManifest, PackageManifestFailure> ParseManifest(string yaml)
	{
		var issues = new List<PackageManifestIssue>();

		if (!TryDeserialize(yaml, issues, out var root))
		{
			return new PackageManifestFailure(issues);
		}

		if (root is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("", "Manifest must be a YAML mapping."));
			return new PackageManifestFailure(issues);
		}

		var doc = Normalize(map);
		foreach (var key in doc.Keys.Where(k => !KnownManifestKeys.Contains(k)))
		{
			issues.Add(ReservedManifestKeys.Contains(key)
				? PackageManifestIssue.Warning(key, $"'{key}' is reserved for a future format version and is ignored.")
				: PackageManifestIssue.Warning(key, $"Unknown key '{key}' is ignored."));
		}

		var format = ReadFormat(doc, issues);

		var name = RequireString(doc, "package", issues);
		if (name is not null && (!PackageSlugRegex().IsMatch(name) || name.Length > MaxPackageIdLength))
		{
			issues.Add(PackageManifestIssue.Error("package",
				$"'{name}' is not a valid package id (lowercase letters, digits, hyphens; must start with a letter; max {MaxPackageIdLength} chars)."));
		}

		var version = ReadVersion(doc, issues);
		var authors = ReadStringList(doc, "authors", issues);
		var description = OptionalString(doc, "description", issues) ?? "";
		var license = OptionalString(doc, "license", issues);
		var homepage = OptionalString(doc, "homepage", issues);
		var keywords = ReadStringList(doc, "keywords", issues);
		if (keywords.Count > MaxKeywords)
		{
			issues.Add(PackageManifestIssue.Warning("keywords",
				$"{keywords.Count} keywords listed; at most {MaxKeywords} are used by the browse UI."));
		}

		var conventionPrefix = OptionalString(doc, "convention_prefix", issues);
		var requiresServer = ReadConstraintField(doc, "requires_server", issues);

		var replaces = OptionalString(doc, "replaces", issues);
		if (replaces is not null && (!PackageSlugRegex().IsMatch(replaces) || replaces == name))
		{
			issues.Add(PackageManifestIssue.Error("replaces",
				replaces == name ? "A package cannot replace itself." : $"'{replaces}' is not a valid package id."));
		}

		var dependencies = ReadRelations(doc, "depends", name, allowSource: true, issues);
		var conflicts = ReadRelations(doc, "conflicts", name, allowSource: false, issues);
		foreach (var conflict in conflicts.Where(c => dependencies.Any(d => d.PackageId == c.PackageId)))
		{
			issues.Add(PackageManifestIssue.Error("conflicts",
				$"'{conflict.PackageId}' is listed in both 'depends' and 'conflicts'."));
		}

		var configure = ReadConfigure(doc, issues);
		var objects = ReadObjects(doc, issues);

		ValidateRefs(objects, configure, dependencies, issues);

		if (issues.Any(i => i.Severity == PackageManifestIssueSeverity.Error))
		{
			return new PackageManifestFailure(issues);
		}

		var manifest = new PackageManifest(
			format,
			name!,
			version!,
			authors,
			description,
			license,
			homepage,
			keywords,
			conventionPrefix,
			requiresServer,
			replaces,
			conflicts,
			dependencies,
			configure,
			objects);

		return new ParsedPackageManifest(manifest, issues);
	}

	public OneOf<PackageIndex, PackageManifestFailure> ParseIndex(string yaml)
	{
		var issues = new List<PackageManifestIssue>();

		if (!TryDeserialize(yaml, issues, out var root))
		{
			return new PackageManifestFailure(issues);
		}

		if (root is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("", "Index must be a YAML mapping."));
			return new PackageManifestFailure(issues);
		}

		var doc = Normalize(map);
		var name = OptionalString(doc, "name", issues);
		var description = OptionalString(doc, "description", issues);

		var entries = new List<PackageIndexEntry>();
		if (doc.TryGetValue("packages", out var packagesNode))
		{
			if (packagesNode is List<object> list)
			{
				for (var i = 0; i < list.Count; i++)
				{
					switch (list[i])
					{
						case string path when path.Trim().Length > 0:
							entries.Add(new PackageIndexEntry(path.Trim(), null));
							break;
						case Dictionary<object, object> entryMap:
							var entry = ReadIndexEntry(Normalize(entryMap), $"packages[{i}]", issues);
							if (entry is not null)
							{
								entries.Add(entry);
							}

							break;
						default:
							issues.Add(PackageManifestIssue.Error($"packages[{i}]",
								"Entry must be a path string or a mapping with 'path'."));
							break;
					}
				}

				var duplicateIds = entries
					.Where(e => e.PackageId is not null)
					.GroupBy(e => e.PackageId!, StringComparer.Ordinal)
					.Where(g => g.Count() > 1);
				foreach (var group in duplicateIds)
				{
					issues.Add(PackageManifestIssue.Error("packages", $"Duplicate package id '{group.Key}' in index."));
				}
			}
			else
			{
				issues.Add(PackageManifestIssue.Error("packages", "'packages' must be a list."));
			}
		}
		else
		{
			issues.Add(PackageManifestIssue.Error("packages", "Index requires a 'packages' list."));
		}

		if (issues.Any(i => i.Severity == PackageManifestIssueSeverity.Error))
		{
			return new PackageManifestFailure(issues);
		}

		return new PackageIndex(name, description, entries);
	}

	public OneOf<CommunityRepoListing, PackageManifestFailure> ParseCommunityListing(string yaml)
	{
		var issues = new List<PackageManifestIssue>();

		if (!TryDeserialize(yaml, issues, out var root))
		{
			return new PackageManifestFailure(issues);
		}

		if (root is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("", "Community listing must be a YAML mapping."));
			return new PackageManifestFailure(issues);
		}

		// Unknown keys are tolerated silently: older servers must keep reading
		// newer listing files.
		var doc = Normalize(map);
		var name = RequireString(doc, "name", issues);
		var url = RequireString(doc, "url", issues);
		var description = RequireString(doc, "description", issues);
		var branch = OptionalString(doc, "branch", issues);
		var homepage = OptionalString(doc, "homepage", issues);
		var maintainers = ReadStringList(doc, "maintainers", issues);

		if (url is not null && !Uri.TryCreate(url, UriKind.Absolute, out _))
		{
			issues.Add(PackageManifestIssue.Error("url", $"'{url}' is not a valid URL."));
		}

		if (issues.Any(i => i.Severity == PackageManifestIssueSeverity.Error))
		{
			return new PackageManifestFailure(issues);
		}

		return new CommunityRepoListing(name!.Trim(), url!.Trim(), branch?.Trim(), description!.Trim(), maintainers, homepage?.Trim());
	}

	private static PackageIndexEntry? ReadIndexEntry(
		Dictionary<string, object?> entry, string path, List<PackageManifestIssue> issues)
	{
		var entryPath = (entry.GetValueOrDefault("path") as string)?.Trim();
		if (string.IsNullOrEmpty(entryPath))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.path", "Entry requires a 'path'."));
			return null;
		}

		PackageVersion? version = null;
		if (entry.GetValueOrDefault("version") is string versionText)
		{
			if (PackageVersion.TryParse(versionText, out var parsed))
			{
				version = parsed;
			}
			else
			{
				issues.Add(PackageManifestIssue.Error($"{path}.version", $"'{versionText}' is not a valid version."));
			}
		}

		var packageId = (entry.GetValueOrDefault("package") as string)?.Trim();
		if (packageId is not null && !PackageSlugRegex().IsMatch(packageId))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.package", $"'{packageId}' is not a valid package id."));
			packageId = null;
		}

		return new PackageIndexEntry(
			entryPath,
			entry.GetValueOrDefault("name") as string,
			packageId,
			version,
			entry.GetValueOrDefault("description") as string);
	}

	private static bool TryDeserialize(string yaml, List<PackageManifestIssue> issues, out object? root)
	{
		root = null;
		if (string.IsNullOrWhiteSpace(yaml))
		{
			issues.Add(PackageManifestIssue.Error("", "Document is empty."));
			return false;
		}

		try
		{
			root = Deserializer.Deserialize<object?>(yaml);
			return true;
		}
		catch (YamlException ex)
		{
			issues.Add(PackageManifestIssue.Error(
				$"line {ex.Start.Line}, column {ex.Start.Column}",
				ex.InnerException?.Message ?? ex.Message));
			return false;
		}
	}

	/// <summary>Converts a raw YAML mapping into a string-keyed dictionary, preserving order.</summary>
	private static Dictionary<string, object?> Normalize(Dictionary<object, object> map)
	{
		var result = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var (key, value) in map)
		{
			result[key.ToString() ?? ""] = value;
		}

		return result;
	}

	private static PackageFormatVersion ReadFormat(Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("format", out var node) || node is null)
		{
			return new PackageFormatVersion(1, 0);
		}

		var text = node.ToString() ?? "";
		var parts = text.Split('.');
		if (parts.Length is < 1 or > 2
			|| !int.TryParse(parts[0], out var major) || major < 1
			|| (parts.Length == 2 && !int.TryParse(parts[1], out _)))
		{
			issues.Add(PackageManifestIssue.Error("format", $"'{text}' is not a valid format version (e.g. 1 or 1.0)."));
			return new PackageFormatVersion(1, 0);
		}

		var minor = parts.Length == 2 ? int.Parse(parts[1]) : 0;
		var format = new PackageFormatVersion(major, minor);
		var supported = PackageFormatVersion.Supported;

		if (format.Major > supported.Major)
		{
			issues.Add(PackageManifestIssue.Error("format",
				$"Format {format} is newer than this server supports ({supported}). Upgrade the server to install this package."));
		}
		else if (format.Major == supported.Major && format.Minor > supported.Minor)
		{
			issues.Add(PackageManifestIssue.Warning("format",
				$"Format {format} is newer than this server fully supports ({supported}); unrecognized fields will be ignored."));
		}

		return format;
	}

	private PackageVersion? ReadVersion(Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		var versionText = RequireString(doc, "version", issues);
		if (versionText is null)
		{
			return null;
		}

		if (PackageVersion.TryParse(versionText, out var parsed))
		{
			return parsed;
		}

		issues.Add(PackageManifestIssue.Error("version", versionText.Contains('+')
			? $"'{versionText}': build metadata (+) is not supported."
			: $"'{versionText}' is not a valid version."));
		return null;
	}

	private static VersionConstraint? ReadConstraintField(
		Dictionary<string, object?> doc, string key, List<PackageManifestIssue> issues)
	{
		var text = OptionalString(doc, key, issues);
		if (text is null)
		{
			return null;
		}

		if (VersionConstraint.TryParse(text, out var constraint))
		{
			return constraint;
		}

		issues.Add(PackageManifestIssue.Error(key, ConstraintError(text)));
		return null;
	}

	private static string ConstraintError(string text) =>
		text.Contains('^') || text.TrimStart().StartsWith('~')
			? $"'{text}': caret/tilde ranges are not supported; write an explicit range such as \">=1.2 <2.0\"."
			: text.Contains('+')
				? $"'{text}': build metadata (+) is not supported in constraints."
				: $"'{text}' is not a valid version constraint.";

	private static string? RequireString(Dictionary<string, object?> doc, string key, List<PackageManifestIssue> issues)
	{
		var value = OptionalString(doc, key, issues);
		if (value is null && !doc.ContainsKey(key))
		{
			issues.Add(PackageManifestIssue.Error(key, $"'{key}' is required."));
		}

		return value;
	}

	private static string? OptionalString(Dictionary<string, object?> doc, string key, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue(key, out var node) || node is null)
		{
			return null;
		}

		if (node is string text)
		{
			return text;
		}

		issues.Add(PackageManifestIssue.Error(key, $"'{key}' must be a string."));
		return null;
	}

	/// <summary>Reads a list of strings; a bare string is accepted as a single-element list.</summary>
	private static IReadOnlyList<string> ReadStringList(
		Dictionary<string, object?> doc, string key, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue(key, out var node) || node is null)
		{
			return [];
		}

		switch (node)
		{
			case string single:
				return [single];
			case List<object> list:
				var result = new List<string>();
				for (var i = 0; i < list.Count; i++)
				{
					if (list[i] is string item)
					{
						result.Add(item);
					}
					else
					{
						issues.Add(PackageManifestIssue.Error($"{key}[{i}]", "Must be a string."));
					}
				}

				return result;
			default:
				issues.Add(PackageManifestIssue.Error(key, $"'{key}' must be a list of strings."));
				return [];
		}
	}

	/// <summary>Reads a depends/conflicts list (decision 20.20: same forms, conflicts carry no source hints).</summary>
	private static IReadOnlyList<PackageDependencySpec> ReadRelations(
		Dictionary<string, object?> doc, string field, string? packageName, bool allowSource,
		List<PackageManifestIssue> issues)
	{
		var relations = new List<PackageDependencySpec>();
		if (!doc.TryGetValue(field, out var node) || node is null)
		{
			return relations;
		}

		if (node is not List<object> list)
		{
			issues.Add(PackageManifestIssue.Error(field, $"'{field}' must be a list."));
			return relations;
		}

		for (var i = 0; i < list.Count; i++)
		{
			var path = $"{field}[{i}]";
			string? id;
			var constraint = VersionConstraint.Any;
			PackageSourceHint? source = null;

			switch (list[i])
			{
				case string bareId:
					id = bareId.Trim();
					break;
				// Full form: mapping with a 'package' key, plus optional 'version' and 'source'.
				case Dictionary<object, object> fullMap when Normalize(fullMap).ContainsKey("package"):
					var full = Normalize(fullMap);
					foreach (var key in full.Keys.Where(k => k is not ("package" or "version" or "source")))
					{
						issues.Add(PackageManifestIssue.Warning($"{path}.{key}", $"Unknown key '{key}' is ignored."));
					}

					id = (full.GetValueOrDefault("package") as string)?.Trim();
					var versionText = full.GetValueOrDefault("version")?.ToString();
					if (!string.IsNullOrWhiteSpace(versionText) && !VersionConstraint.TryParse(versionText, out constraint))
					{
						issues.Add(PackageManifestIssue.Error($"{path}.version", ConstraintError(versionText)));
					}

					if (full.GetValueOrDefault("source") is { } sourceNode)
					{
						if (allowSource)
						{
							source = ReadSourceHint(sourceNode, path, issues);
						}
						else
						{
							issues.Add(PackageManifestIssue.Warning($"{path}.source",
								$"'source' has no meaning under '{field}' and is ignored."));
						}
					}

					break;
				// Shorthand: single-key mapping of id to version constraint.
				case Dictionary<object, object> entryMap when entryMap.Count == 1:
					var (rawId, rawConstraint) = entryMap.First();
					id = rawId.ToString()?.Trim();
					var constraintText = rawConstraint?.ToString();
					if (!string.IsNullOrWhiteSpace(constraintText) && !VersionConstraint.TryParse(constraintText, out constraint))
					{
						issues.Add(PackageManifestIssue.Error(path, ConstraintError(constraintText)));
					}

					break;
				default:
					issues.Add(PackageManifestIssue.Error(path,
						"Entry must be a package id, a single-key mapping of id to version constraint, "
						+ "or a mapping with 'package', optional 'version', and optional 'source'."));
					continue;
			}

			if (string.IsNullOrEmpty(id) || !PackageSlugRegex().IsMatch(id))
			{
				issues.Add(PackageManifestIssue.Error(path, $"'{id}' is not a valid package id."));
				continue;
			}

			if (id == packageName)
			{
				issues.Add(PackageManifestIssue.Error(path, $"A package cannot appear in its own '{field}'."));
				continue;
			}

			if (relations.Any(d => d.PackageId == id))
			{
				issues.Add(PackageManifestIssue.Error(path, $"Duplicate entry '{id}'."));
				continue;
			}

			relations.Add(new PackageDependencySpec(id, constraint, source));
		}

		return relations;
	}

	/// <summary>
	/// Reads a dependency's source hint: either a bare repo URL string or a
	/// mapping with 'repo' and optional 'path' / 'branch'.
	/// </summary>
	private static PackageSourceHint? ReadSourceHint(object? node, string parentPath, List<PackageManifestIssue> issues)
	{
		switch (node)
		{
			case null:
				return null;
			case string repo when repo.Trim().Length > 0:
				return new PackageSourceHint(repo.Trim(), null, null);
			case Dictionary<object, object> sourceMap:
				var source = Normalize(sourceMap);
				foreach (var key in source.Keys.Where(k => k is not ("repo" or "path" or "branch")))
				{
					issues.Add(PackageManifestIssue.Warning($"{parentPath}.source.{key}", $"Unknown key '{key}' is ignored."));
				}

				var repoUrl = (source.GetValueOrDefault("repo") as string)?.Trim();
				if (string.IsNullOrEmpty(repoUrl))
				{
					issues.Add(PackageManifestIssue.Error($"{parentPath}.source", "Source requires a 'repo' URL."));
					return null;
				}

				return new PackageSourceHint(
					repoUrl,
					(source.GetValueOrDefault("path") as string)?.Trim(),
					(source.GetValueOrDefault("branch") as string)?.Trim());
			default:
				issues.Add(PackageManifestIssue.Error($"{parentPath}.source",
					"Source must be a repo URL string or a mapping with 'repo', optional 'path', and optional 'branch'."));
				return null;
		}
	}

	private static IReadOnlyDictionary<string, PackageConfigureSpec> ReadConfigure(
		Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		var configure = new Dictionary<string, PackageConfigureSpec>(StringComparer.Ordinal);
		if (!doc.TryGetValue("configure", out var node) || node is null)
		{
			return configure;
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("configure", "'configure' must be a mapping of ref name to declaration."));
			return configure;
		}

		foreach (var (rawKey, rawValue) in Normalize(map))
		{
			var key = rawKey.ToLowerInvariant();
			var path = $"configure.{rawKey}";
			if (!RefNameRegex().IsMatch(key))
			{
				issues.Add(PackageManifestIssue.Error(path,
					$"'{rawKey}' is not a valid ref name (lowercase letters, digits, underscores)."));
				continue;
			}

			if (configure.ContainsKey(key))
			{
				issues.Add(PackageManifestIssue.Error(path, $"Duplicate configure ref '{key}'."));
				continue;
			}

			string? label;
			var type = PackageConfigureType.Dbref;
			string? defaultValue = null;

			switch (rawValue)
			{
				case string text:
					label = text;
					break;
				case Dictionary<object, object> rawEntry:
					var entry = Normalize(rawEntry);
					foreach (var entryKey in entry.Keys.Where(k => k is not ("label" or "type" or "default")))
					{
						issues.Add(entryKey == "pattern"
							? PackageManifestIssue.Warning($"{path}.pattern", "'pattern' is reserved for a future format version and is ignored.")
							: PackageManifestIssue.Warning($"{path}.{entryKey}", $"Unknown key '{entryKey}' is ignored."));
					}

					label = entry.GetValueOrDefault("label") as string;
					if (entry.GetValueOrDefault("type") is string typeText)
					{
						if (!Enum.TryParse(typeText, ignoreCase: true, out type))
						{
							issues.Add(PackageManifestIssue.Error($"{path}.type",
								$"'{typeText}' is not a valid configure type (dbref, string, number, boolean)."));
							continue;
						}
					}

					defaultValue = entry.GetValueOrDefault("default")?.ToString();
					break;
				default:
					issues.Add(PackageManifestIssue.Error(path, "Configure ref must be a label string or a mapping."));
					continue;
			}

			if (string.IsNullOrWhiteSpace(label))
			{
				issues.Add(PackageManifestIssue.Error(path, "Configure ref requires a label."));
				continue;
			}

			if (!ValidateConfigureDefault(type, defaultValue, path, issues))
			{
				continue;
			}

			configure[key] = new PackageConfigureSpec(key, label, type, defaultValue);
		}

		return configure;
	}

	private static bool ValidateConfigureDefault(
		PackageConfigureType type, string? defaultValue, string path, List<PackageManifestIssue> issues)
	{
		if (defaultValue is null)
		{
			return true;
		}

		switch (type)
		{
			case PackageConfigureType.Dbref:
				issues.Add(PackageManifestIssue.Error($"{path}.default",
					"dbref configure refs cannot declare a default — dbrefs are game-specific, which is the point of configure."));
				return false;
			case PackageConfigureType.Number when !decimal.TryParse(defaultValue, NumberStyles.Number, CultureInfo.InvariantCulture, out _):
				issues.Add(PackageManifestIssue.Error($"{path}.default", $"'{defaultValue}' is not a valid number."));
				return false;
			case PackageConfigureType.Boolean when defaultValue is not ("true" or "false"):
				issues.Add(PackageManifestIssue.Error($"{path}.default", "Boolean defaults must be 'true' or 'false'."));
				return false;
			default:
				return true;
		}
	}

	private static IReadOnlyList<PackageObjectSpec> ReadObjects(
		Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		var objects = new List<PackageObjectSpec>();
		if (!doc.TryGetValue("objects", out var node) || node is null)
		{
			issues.Add(PackageManifestIssue.Error("objects", "'objects' is required."));
			return objects;
		}

		if (node is not List<object> list || list.Count == 0)
		{
			issues.Add(PackageManifestIssue.Error("objects", "'objects' must be a non-empty list."));
			return objects;
		}

		var seenRefs = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i < list.Count; i++)
		{
			var path = $"objects[{i}]";
			if (list[i] is not Dictionary<object, object> rawObject)
			{
				issues.Add(PackageManifestIssue.Error(path, "Object entry must be a mapping."));
				continue;
			}

			var obj = Normalize(rawObject);
			foreach (var key in obj.Keys.Where(k => !KnownObjectKeys.Contains(k)))
			{
				issues.Add(PackageManifestIssue.Warning($"{path}.{key}", $"Unknown key '{key}' is ignored."));
			}

			var refName = (obj.GetValueOrDefault("ref") as string)?.Trim().ToLowerInvariant();
			if (string.IsNullOrEmpty(refName) || !RefNameRegex().IsMatch(refName))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.ref",
					$"'{refName}' is not a valid ref name (lowercase letters, digits, underscores)."));
				continue;
			}

			if (!seenRefs.Add(refName))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.ref", $"Duplicate object ref '{refName}'."));
				continue;
			}

			// Attach mode (decision 20.3): manage attributes on an existing
			// object (well-known or configure) instead of creating one.
			if (obj.ContainsKey("target"))
			{
				var attachSpec = ReadAttachObject(obj, refName, path, issues);
				if (attachSpec is not null)
				{
					objects.Add(attachSpec);
				}

				continue;
			}

			var typeText = obj.GetValueOrDefault("type") as string;
			if (typeText is null || !Enum.TryParse<PackageObjectType>(typeText, ignoreCase: true, out var type))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.type",
					$"'{typeText}' is not a valid object type (thing, room, exit, player)."));
				continue;
			}

			var objectName = obj.GetValueOrDefault("name") as string;
			if (string.IsNullOrWhiteSpace(objectName))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", "Object requires a name."));
				continue;
			}

			var parent = ReadRefField(obj, "parent", path, issues);
			var location = ReadRefField(obj, "location", path, issues);
			var destination = ReadRefField(obj, "destination", path, issues);

			switch (type)
			{
				case PackageObjectType.Exit:
					if (location is null && !obj.ContainsKey("location"))
					{
						issues.Add(PackageManifestIssue.Error($"{path}.location", "Exits require a 'location' (source room)."));
					}

					if (destination is null && !obj.ContainsKey("destination"))
					{
						issues.Add(PackageManifestIssue.Error($"{path}.destination", "Exits require a 'destination'."));
					}

					break;
				case PackageObjectType.Room when obj.ContainsKey("location"):
					issues.Add(PackageManifestIssue.Error($"{path}.location", "Rooms cannot have a 'location'."));
					break;
				default:
					if (obj.ContainsKey("destination"))
					{
						issues.Add(PackageManifestIssue.Error($"{path}.destination", "Only exits may declare a 'destination'."));
					}

					break;
			}

			var previousRefs = ReadStringList(obj, "previous_refs", issues)
				.Select(r => r.ToLowerInvariant())
				.ToList();
			foreach (var previous in previousRefs.Where(p => !RefNameRegex().IsMatch(p)))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.previous_refs", $"'{previous}' is not a valid ref name."));
			}

			var flags = ReadStringList(obj, "flags", issues);
			var locks = ReadStringMap(obj, "locks", path, issues);
			var attributes = ReadAttributes(obj, path, issues);

			objects.Add(new PackageObjectSpec(
				refName, type, objectName.Trim(), null, parent, location, destination, previousRefs, flags, locks, attributes));
		}

		return objects;
	}

	/// <summary>
	/// Parses an attach-mode object: a <c>target</c> ref plus attributes only.
	/// Creation-only keys (type/name/parent/location/destination/flags/locks/
	/// previous_refs) are rejected — an attach object manages attributes on an
	/// existing object and never restructures or destroys it.
	/// </summary>
	private static PackageObjectSpec? ReadAttachObject(
		Dictionary<string, object?> obj, string refName, string path, List<PackageManifestIssue> issues)
	{
		foreach (var key in obj.Keys.Where(AttachForbiddenKeys.Contains))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.{key}",
				$"'{key}' is not allowed on a 'target' (attach) object — attach objects manage only attributes."));
		}

		var target = PackageRefScanner.ParseSingle(obj.GetValueOrDefault("target") as string ?? "");
		if (target is null)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.target",
				"'target' must be a single ref ({{$well_known}} or {{?configure}})."));
			return null;
		}

		if (target.Kind is not (PackageRefKind.WellKnown or PackageRefKind.Configure))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.target",
				$"'target' must be a {{{{$well_known}}}} or {{{{?configure}}}} ref, not '{target}'."));
			return null;
		}

		var attributes = ReadAttributes(obj, path, issues);
		if (attributes.Count == 0)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.attributes",
				"An attach object must declare at least one attribute."));
			return null;
		}

		return new PackageObjectSpec(
			refName, PackageObjectType.Thing, "", target, null, null, null, [], [],
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), attributes);
	}

	private static PackageRef? ReadRefField(
		Dictionary<string, object?> obj, string key, string parentPath, List<PackageManifestIssue> issues)
	{
		if (obj.GetValueOrDefault(key) is not string text)
		{
			return null;
		}

		var reference = PackageRefScanner.ParseSingle(text);
		if (reference is null)
		{
			issues.Add(PackageManifestIssue.Error($"{parentPath}.{key}",
				$"'{text}' is not a valid ref ({{{{name}}}}, {{{{$well_known}}}}, {{{{?configure}}}}, or {{{{pkg/ref}}}})."));
		}

		return reference;
	}

	private static IReadOnlyDictionary<string, string> ReadStringMap(
		Dictionary<string, object?> obj, string key, string parentPath, List<PackageManifestIssue> issues)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!obj.TryGetValue(key, out var node) || node is null)
		{
			return result;
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error($"{parentPath}.{key}", $"'{key}' must be a mapping."));
			return result;
		}

		foreach (var (rawKey, rawValue) in Normalize(map))
		{
			if (rawValue is string text)
			{
				result[rawKey] = text;
			}
			else
			{
				issues.Add(PackageManifestIssue.Error($"{parentPath}.{key}.{rawKey}", "Value must be a string."));
			}
		}

		return result;
	}

	private static IReadOnlyDictionary<string, PackageAttributeSpec> ReadAttributes(
		Dictionary<string, object?> obj, string parentPath, List<PackageManifestIssue> issues)
	{
		var attributes = new Dictionary<string, PackageAttributeSpec>(StringComparer.OrdinalIgnoreCase);
		if (!obj.TryGetValue("attributes", out var node) || node is null)
		{
			return attributes;
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error($"{parentPath}.attributes", "'attributes' must be a mapping."));
			return attributes;
		}

		foreach (var (attrName, rawValue) in Normalize(map))
		{
			var path = $"{parentPath}.attributes.{attrName}";
			if (!AttributeNameRegex().IsMatch(attrName) || attrName.StartsWith('`') || attrName.EndsWith('`'))
			{
				issues.Add(PackageManifestIssue.Error(path,
					"Attribute name must be non-empty without whitespace; attribute-tree backticks may only appear between segments."));
				continue;
			}

			if (PackageRefIndirection.IsReservedAttribute(attrName))
			{
				issues.Add(PackageManifestIssue.Error(path,
					"The PM` attribute tree is reserved for the package engine (ref indirection, decision 20.21)."));
				continue;
			}

			if (attributes.ContainsKey(attrName))
			{
				issues.Add(PackageManifestIssue.Error(path, $"Duplicate attribute '{attrName}'."));
				continue;
			}

			switch (rawValue)
			{
				// Shorthand: ATTR: "value"
				case string text:
					attributes[attrName] = new PackageAttributeSpec(text, []);
					break;
				case Dictionary<object, object> entryMap:
					var entry = Normalize(entryMap);
					if (entry.GetValueOrDefault("value") is not string value)
					{
						issues.Add(PackageManifestIssue.Error(path, "Attribute requires a string 'value'."));
						break;
					}

					attributes[attrName] = new PackageAttributeSpec(value, ReadStringList(entry, "flags", issues));
					break;
				default:
					issues.Add(PackageManifestIssue.Error(path,
						"Attribute must be a string value or a mapping with 'value' and optional 'flags'. "
						+ "If the value looked like a string, YAML may have re-parsed it — write MUSHcode as a block scalar (value: |-)."));
					break;
			}
		}

		return attributes;
	}

	/// <summary>
	/// Cross-object validation (decision 20.11): every {{token}} must be a
	/// valid, resolvable ref — malformed bodies, unresolved internal refs,
	/// unknown well-known names, undeclared configure refs, and cross-package
	/// refs to undeclared dependencies are all hard errors. Also enforces
	/// configure typing rules (20.19), previous_refs collision checks (20.15),
	/// and parent-cycle detection.
	/// </summary>
	private void ValidateRefs(
		IReadOnlyList<PackageObjectSpec> objects,
		IReadOnlyDictionary<string, PackageConfigureSpec> configure,
		IReadOnlyList<PackageDependencySpec> dependencies,
		List<PackageManifestIssue> issues)
	{
		var definedRefs = objects.Select(o => o.Ref).ToHashSet(StringComparer.Ordinal);
		var dependencyIds = dependencies.Select(d => d.PackageId).ToHashSet(StringComparer.Ordinal);
		var usedConfigureKeys = new HashSet<string>(StringComparer.Ordinal);
		var usedWellKnownNames = new HashSet<string>(StringComparer.Ordinal);

		void CheckRef(PackageRef reference, string path, bool requiresDbref)
		{
			switch (reference.Kind)
			{
				case PackageRefKind.Internal when reference.Package is not null:
					if (!dependencyIds.Contains(reference.Package))
					{
						issues.Add(PackageManifestIssue.Error(path,
							$"'{reference}' references package '{reference.Package}', which is not listed under 'depends'."));
					}

					break;
				case PackageRefKind.Internal when !definedRefs.Contains(reference.Name):
					issues.Add(PackageManifestIssue.Error(path,
						$"'{reference}' does not match any object ref in this package."));
					break;
				case PackageRefKind.WellKnown when !_wellKnownRefs.Contains(reference.Name):
					issues.Add(PackageManifestIssue.Error(path,
						$"'{reference}' is not a recognized well-known ref ({string.Join(", ", _wellKnownRefs.Order())})."));
					break;
				case PackageRefKind.WellKnown:
					usedWellKnownNames.Add(reference.Name);
					break;
				case PackageRefKind.Configure:
					usedConfigureKeys.Add(reference.Name);
					if (!configure.TryGetValue(reference.Name, out var spec))
					{
						issues.Add(PackageManifestIssue.Error(path,
							$"'{reference}' is not declared under 'configure'."));
					}
					else if (requiresDbref && spec.Type != PackageConfigureType.Dbref)
					{
						issues.Add(PackageManifestIssue.Error(path,
							$"'{reference}' is declared as type '{spec.Type.ToString().ToLowerInvariant()}' but this field requires a dbref-typed configure ref."));
					}

					break;
			}
		}

		void ScanText(string text, string path)
		{
			foreach (var token in PackageRefScanner.Scan(text))
			{
				if (token.Ref is null)
				{
					issues.Add(PackageManifestIssue.Error(path,
						$"'{token.Raw}' is not a valid ref. Escape a literal '{{{{' as '{{{{{{{{'."));
				}
				else
				{
					CheckRef(token.Ref, path, requiresDbref: false);
				}
			}
		}

		for (var i = 0; i < objects.Count; i++)
		{
			var obj = objects[i];
			var path = $"objects[{i}]";

			foreach (var (fieldName, reference) in new[]
				{ ("target", obj.Target), ("parent", obj.Parent), ("location", obj.Location), ("destination", obj.Destination) })
			{
				if (reference is not null)
				{
					CheckRef(reference, $"{path}.{fieldName}", requiresDbref: true);
				}
			}

			foreach (var previous in obj.PreviousRefs.Where(definedRefs.Contains))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.previous_refs",
					$"'{previous}' is also a current object ref; previous_refs may only contain retired names."));
			}

			foreach (var (attrName, attr) in obj.Attributes)
			{
				ScanText(attr.Value, $"{path}.attributes.{attrName}");
			}

			foreach (var (lockName, lockValue) in obj.Locks)
			{
				ScanText(lockValue, $"{path}.locks.{lockName}");
			}
		}

		foreach (var unused in configure.Keys.Where(k => !usedConfigureKeys.Contains(k)))
		{
			issues.Add(PackageManifestIssue.Warning($"configure.{unused}",
				$"Configure ref '{{{{?{unused}}}}}' is declared but never used."));
		}

		// Same-package refs of every kind share the PM`REFS`<NAME> indirection
		// namespace (decision 20.21) — cross-kind name collisions are errors.
		foreach (var name in definedRefs.Intersect(configure.Keys, StringComparer.Ordinal))
		{
			issues.Add(PackageManifestIssue.Error("configure",
				$"'{name}' is both an object ref and a configure key; they would collide in the PM`REFS`{name.ToUpperInvariant()} indirection attribute."));
		}

		foreach (var name in definedRefs.Intersect(usedWellKnownNames, StringComparer.Ordinal))
		{
			issues.Add(PackageManifestIssue.Error("objects",
				$"Object ref '{name}' collides with the well-known ref '{{{{${name}}}}}' in the PM`REFS`{name.ToUpperInvariant()} indirection attribute."));
		}

		foreach (var name in configure.Keys.Intersect(usedWellKnownNames, StringComparer.Ordinal))
		{
			issues.Add(PackageManifestIssue.Error("configure",
				$"Configure key '{name}' collides with the well-known ref '{{{{${name}}}}}' in the PM`REFS`{name.ToUpperInvariant()} indirection attribute."));
		}

		DetectParentCycles(objects, issues);
	}

	/// <summary>Reports any cycle formed by intra-package parent references.</summary>
	private static void DetectParentCycles(IReadOnlyList<PackageObjectSpec> objects, List<PackageManifestIssue> issues)
	{
		var parentByRef = objects
			.Where(o => o.Parent is { Kind: PackageRefKind.Internal, Package: null })
			.ToDictionary(o => o.Ref, o => o.Parent!.Name, StringComparer.Ordinal);

		var reported = new HashSet<string>(StringComparer.Ordinal);
		foreach (var start in parentByRef.Keys)
		{
			var seen = new HashSet<string>(StringComparer.Ordinal) { start };
			var current = start;
			while (parentByRef.TryGetValue(current, out var next))
			{
				if (!seen.Add(next))
				{
					var cycle = string.Join(" -> ", seen.Append(next).Select(r => $"{{{{{r}}}}}"));
					if (reported.Add(next))
					{
						issues.Add(PackageManifestIssue.Error("objects", $"Parent cycle detected: {cycle}."));
					}

					break;
				}

				current = next;
			}
		}
	}
}
