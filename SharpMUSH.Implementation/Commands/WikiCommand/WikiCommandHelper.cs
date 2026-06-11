using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// Shared helpers for @wiki subcommands and wiki() functions:
/// page-target resolution ("Help:Getting Started" → namespace + slug),
/// edit-permission checks, and listing line formatting.
/// </summary>
public static class WikiCommandHelper
{
	/// <summary>
	/// Resolves a user-supplied page target into a (namespace, category, slug) identity.
	/// Accepts the same forms as <c>[[wiki links]]</c>: a bare title ("Getting Started"),
	/// a namespace-prefixed one ("Help:Getting Started" → category general), or a fully
	/// qualified one ("Help:Guides:Getting Started"). Unknown namespace prefixes are treated
	/// as part of a Main-namespace title.
	/// </summary>
	public static (WikiNamespace Namespace, string Category, string Slug) ResolveTarget(string target)
	{
		var trimmed = target.Trim();
		var parts = trimmed.Split(':', 3);

		if (parts.Length == 3
			&& Enum.TryParse<WikiNamespace>(parts[0].Trim(), ignoreCase: true, out var ns3))
		{
			return (ns3, WikiHelpers.NormalizeCategory(parts[1]), WikiHelpers.Slugify(parts[2].Trim()));
		}

		if (parts.Length == 2
			&& Enum.TryParse<WikiNamespace>(parts[0].Trim(), ignoreCase: true, out var ns2))
		{
			return (ns2, WikiHelpers.DefaultCategory, WikiHelpers.Slugify(parts[1].Trim()));
		}

		return (WikiNamespace.Main, WikiHelpers.DefaultCategory, WikiHelpers.Slugify(trimmed));
	}

	/// <summary>
	/// The display form of a page reference: "slug" for a Main/general page, "ns:category:slug"
	/// otherwise. This round-trips through <see cref="ResolveTarget"/>.
	/// </summary>
	public static string DisplayReference(WikiPage page)
	{
		var cat = page.Category ?? WikiHelpers.DefaultCategory;
		return page.Namespace.Equals("main", StringComparison.OrdinalIgnoreCase)
			&& cat.Equals(WikiHelpers.DefaultCategory, StringComparison.OrdinalIgnoreCase)
				? page.Slug
				: $"{page.Namespace}:{cat}:{page.Slug}";
	}

	/// <summary>
	/// Edit permission mirrors the web rule: protected pages are Wizard-only;
	/// everything else is editable by any player.
	/// </summary>
	public static async ValueTask<bool> CanEdit(AnySharpObject executor, WikiPage page) =>
		!page.IsProtected || await executor.IsWizard();

	/// <summary>The executor's dbref string as stored in wiki author/editor fields.</summary>
	public static string EditorDbref(AnySharpObject executor) =>
		$"#{executor.Object().Key}";

	/// <summary>One listing line: "reference — Title (rev N, yyyy-MM-dd)" plus draft/protected markers.</summary>
	public static string FormatPageLine(WikiPage page)
	{
		var markers = $"{(page.Published ? "" : " (draft)")}{(page.IsProtected ? " (protected)" : "")}";
		return $"{DisplayReference(page),-30} {page.Title} (rev {page.RevisionNumber}, {page.UpdatedAt:yyyy-MM-dd}){markers}";
	}
}
