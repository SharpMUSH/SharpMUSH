namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// How many pages the wiki holds by state, counted from the store's list indexes without reading a page.
/// </summary>
/// <param name="Published">Pages whose published flag is set (or absent, which reads as published).</param>
/// <param name="Drafts">Unpublished pages; zero when the count excluded drafts.</param>
/// <param name="Protected">Protected pages among those counted, so drafts are left out when they were.</param>
public readonly record struct WikiPageCounts(int Published, int Drafts, int Protected)
{
	/// <summary>Every page counted.</summary>
	public int Total => Published + Drafts;
}
