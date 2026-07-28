namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// Why a translation write lost a race with another writer. Every member is a <em>lost write</em>: the
/// request was well-formed, somebody else got there first, and the only safe response is to reload — which
/// is why the HTTP boundary answers all of them with 409 and none of them with 400.
/// </summary>
/// <remarks>
/// This exists as a type rather than a phrase because the four <c>IWikiService</c> implementations each
/// detect these cases separately. Classifying them by matching on error text made the status code depend on
/// four backends wording their messages identically, which they did only by coincidence — and one of them
/// (Memgraph) had already drifted. The compiler checks this instead.
/// </remarks>
public enum WikiWriteConflict
{
	/// <summary>
	/// The caller's <c>expectedRevisionNumber</c> no longer matches the stored one: another editor saved
	/// first. Never retried — a retry re-applies the loser's stale markdown over the winner's.
	/// </summary>
	StaleRevision,

	/// <summary>
	/// A create-only write (null <c>expectedRevisionNumber</c>) found a translation already there. The
	/// caller believed it was creating the row, so overwriting it would be a blind clobber.
	/// </summary>
	AlreadyExists,

	/// <summary>
	/// The caller passed an <c>expectedRevisionNumber</c> for a translation that has since been deleted.
	/// A lost write like any other: re-creating it would resurrect a row somebody deliberately removed.
	/// </summary>
	TranslationGone
}
