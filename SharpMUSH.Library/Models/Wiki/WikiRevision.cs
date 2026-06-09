namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A historical snapshot of a wiki page at a specific revision.
/// Used for diff/history/revert.  Full snapshots rather than diffs (simpler for v1).
/// </summary>
/// <param name="Id">Storage key.</param>
/// <param name="PageId">FK to the parent WikiPage.Id.</param>
/// <param name="RevisionNumber">The revision number this snapshot corresponds to.</param>
/// <param name="MarkdownSource">Full markdown body at this revision.</param>
/// <param name="EditorDbref">DBRef string of the player who made this revision.</param>
/// <param name="Timestamp">UTC timestamp when this revision was saved.</param>
/// <param name="EditSummary">Optional human-readable edit summary ("Fixed typo in combat rules").</param>
public record WikiRevision(
	string Id,
	string PageId,
	int RevisionNumber,
	string MarkdownSource,
	string EditorDbref,
	DateTimeOffset Timestamp,
	string? EditSummary);
