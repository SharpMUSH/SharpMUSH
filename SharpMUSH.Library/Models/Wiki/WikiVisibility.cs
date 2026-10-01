namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// Which pages a listing returns: published pages, plus every draft when <see cref="IncludeDrafts"/>,
/// plus the drafts <see cref="AuthorDbref"/> wrote. A store applies it before <c>skip</c>/<c>take</c>, so
/// a page of a listing is a page of rows the reader may see; filtering afterwards returned short or
/// empty pages whenever hidden drafts sorted first.
/// </summary>
/// <param name="IncludeDrafts">True to return every draft.</param>
/// <param name="AuthorDbref">The reader's dbref, whose own drafts are returned; null for none.</param>
public readonly record struct WikiVisibility(bool IncludeDrafts, string? AuthorDbref = null)
{
	/// <summary>Every page, drafts included.</summary>
	public static WikiVisibility All => new(IncludeDrafts: true);

	/// <summary>Published pages only.</summary>
	public static WikiVisibility PublishedOnly => new(IncludeDrafts: false);

	/// <summary>True when a page with this flag and author is returned.</summary>
	public bool Admits(bool published, string? authorDbref) =>
		published
		|| IncludeDrafts
		|| (AuthorDbref is { Length: > 0 } me && string.Equals(authorDbref, me, StringComparison.Ordinal));
}
