using Markdig;
using Markdig.Syntax;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Checks the wiki links in a package page's body. A package page names every page it links to in full,
/// <c>[[Display|namespace:slug]]</c>, so a link means the same page whatever the game titles it and whichever
/// language reads it: a bare <c>[[Blinded]]</c>, or a title such as <c>Help:ORC License Notice</c>, is an error.
/// A full link to a page outside the package is allowed; one that looks like a typo for a page the package ships
/// is a warning.
/// </summary>
/// <remarks>
/// The links are found by the wiki's own Markdown pipeline (<see cref="WikiLinkExtension"/>), so a link the
/// renderer would not draw — inside a code span or block, say — is not checked either.
/// </remarks>
public static class PackageWikiLinks
{
	private static readonly MarkdownPipeline Pipeline = WikiMarkdigPipeline.CreatePipeline();

	/// <summary>
	/// The issues with <paramref name="page"/>'s links, reported against <paramref name="source"/> (the body's file,
	/// or the manifest entry of an inline body). <paramref name="declared"/> is every page the manifest ships.
	/// </summary>
	public static IEnumerable<PackageManifestIssue> Check(
		PackageWikiPageSpec page, IReadOnlyList<PackageWikiPageSpec> declared, string source)
	{
		if (page.Markdown is not { Length: > 0 } markdown)
		{
			yield break;
		}

		var shipped = declared.Select(p => (p.Namespace, p.Page)).ToHashSet();
		foreach (var link in Markdown.Parse(markdown, Pipeline).Descendants<WikiLinkInline>())
		{
			var written = Written(link);
			if (FullTarget(link.Target) is not { } target)
			{
				var (ns, slug) = WikiHelpers.ResolveTitle(link.Target);
				yield return PackageManifestIssue.Error(source,
					$"The link {written} must name its page in full, namespace:slug, such as "
					+ $"[[{link.DisplayText ?? WikiHelpers.SplitTitle(link.Target).Title}|{WikiHelpers.NamespaceName(ns)}:{slug}]].");
				continue;
			}

			if (!shipped.Contains(target) && Intended(target, declared) is { } meant)
			{
				yield return PackageManifestIssue.Warning(source,
					$"The link {written} names {WikiHelpers.NamespaceName(target.Namespace)}:{target.Slug}, which this package "
					+ $"does not ship; did you mean {meant.Label}?");
			}
		}
	}

	/// <summary>
	/// The page <paramref name="target"/> names when it is written in full: a namespace prefix (any case) and a slug
	/// the wiki keeps as it is. Null for a bare title, or a slug in title form.
	/// </summary>
	public static (WikiNamespace Namespace, string Slug)? FullTarget(string target)
	{
		var trimmed = target.Trim();
		var (ns, inner) = WikiHelpers.SplitTitle(trimmed);
		var prefixed = inner.Length < trimmed.Length;
		return prefixed && inner.Length > 0 && WikiHelpers.Slugify(inner) == inner && !inner.Any(char.IsWhiteSpace)
			? (ns, inner)
			: null;
	}

	/// <summary>
	/// The shipped page a link to an unshipped <paramref name="target"/> most likely meant: the same slug in another
	/// namespace, or the page in that namespace whose title is the slug. Null when nothing looks like it.
	/// </summary>
	private static PackageWikiPageSpec? Intended((WikiNamespace Namespace, string Slug) target, IReadOnlyList<PackageWikiPageSpec> declared)
		=> declared.FirstOrDefault(p => p.Page == target.Slug)
			?? declared.FirstOrDefault(p => p.Namespace == target.Namespace
				&& (WikiHelpers.Slugify(p.Title) == target.Slug
					|| string.Equals(p.Page.Replace('_', '-'), target.Slug.Replace('_', '-'), StringComparison.Ordinal)));

	private static string Written(WikiLinkInline link)
		=> link.DisplayText is { } display ? $"[[{display}|{link.Target}]]" : $"[[{link.Target}]]";
}
