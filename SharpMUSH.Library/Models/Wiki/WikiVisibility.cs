namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// Which pages a listing returns: published pages, plus every draft when <see cref="IncludeDrafts"/>,
/// plus the drafts <see cref="AuthorDbref"/> wrote, less the pages <see cref="Hidden"/> keeps from this
/// reader. A store applies it before <c>skip</c>/<c>take</c>, so a page of a listing is a page of rows the
/// reader may see; filtering afterwards returned short or empty pages whenever hidden drafts sorted first.
/// </summary>
/// <param name="IncludeDrafts">True to return every draft.</param>
/// <param name="AuthorDbref">The reader's dbref, whose own drafts are returned; null for none.</param>
public readonly record struct WikiVisibility(bool IncludeDrafts, string? AuthorDbref = null)
{
	/// <summary>Every page, drafts included.</summary>
	public static WikiVisibility All => new(IncludeDrafts: true);

	/// <summary>Published pages only.</summary>
	public static WikiVisibility PublishedOnly => new(IncludeDrafts: false);

	/// <summary>
	/// The namespaces, categories and pages whose read requirements this reader does not meet; null when
	/// it meets them all. A store that sees one has to read each candidate row to apply it.
	/// </summary>
	public WikiReadRestrictions? Hidden { get; init; }

	/// <summary>True when a page with this flag and author passes the draft rule.</summary>
	public bool Admits(bool published, string? authorDbref) =>
		published
		|| IncludeDrafts
		|| (AuthorDbref is { Length: > 0 } me && string.Equals(authorDbref, me, StringComparison.Ordinal));

	/// <summary>True when <paramref name="page"/> is returned: it passes the draft rule and is not hidden.</summary>
	public bool Admits(WikiPage page)
		=> Admits(page.Published, page.AuthorDbref) && Hidden?.Hides(page.Namespace, page.Categories, page.Id) != true;
}

/// <summary>
/// What a reader may not read (see <see cref="WikiVisibility.Hidden"/>): everything when
/// <see cref="Everything"/>, which is a reader without <c>wiki.read</c>, else the pages in any of these
/// namespaces or categories and these pages.
/// </summary>
public sealed record WikiReadRestrictions(
	bool Everything,
	IReadOnlySet<string> Namespaces,
	IReadOnlySet<string> Categories,
	IReadOnlySet<string> PageIds)
{
	/// <summary>Hides every page.</summary>
	public static WikiReadRestrictions All { get; } = new(true,
		new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

	/// <summary>True when a page with this namespace, these categories and this id is hidden.</summary>
	public bool Hides(string ns, IEnumerable<string> categories, string pageId)
		=> Everything
			|| Namespaces.Contains(ns)
			|| PageIds.Contains(pageId)
			|| categories.Any(Categories.Contains);
}
