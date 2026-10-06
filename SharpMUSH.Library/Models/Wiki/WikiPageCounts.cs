namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// How many pages a reader may see by state, counted from the store's list indexes.
/// </summary>
/// <param name="Published">Pages whose published flag is set (or absent, which reads as published).</param>
/// <param name="Drafts">Unpublished pages the reader may see.</param>
public readonly record struct WikiPageCounts(int Published, int Drafts)
{
	/// <summary>Every page counted.</summary>
	public int Total => Published + Drafts;
}
