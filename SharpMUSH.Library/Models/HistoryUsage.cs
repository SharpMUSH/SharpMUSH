namespace SharpMUSH.Library.Models;

/// <summary>
/// How much of one kind of history the live world holds.
/// </summary>
/// <param name="Kind">The kind, as its store names it (<c>wiki</c>, <c>scene.edits</c>, …).</param>
/// <param name="Description">What the records are, for a wizard reading the report.</param>
/// <param name="Holders">How many pages, poses or other owners the records belong to.</param>
/// <param name="Records">How many history records there are.</param>
/// <param name="Bytes">Their stored size, keys and values together. Pages on disk run somewhat larger:
/// LMDB keeps each table in its own B-tree of fixed-size pages, and a page is rarely full.</param>
public sealed record HistoryUsage(string Kind, string Description, long Holders, long Records, long Bytes);

/// <summary>
/// One record a retention pass has chosen to purge: enough to delete it again under a fresh check, and
/// what to archive before it goes.
/// </summary>
/// <param name="Key">The record's key in its store. Opaque outside it.</param>
/// <param name="Bytes">What deleting it frees, keys and values together.</param>
/// <param name="Archive">The record as JSON, appended to the archive (when one is configured) before
/// the delete commits.</param>
public sealed record HistoryPurgeCandidate(byte[] Key, long Bytes, byte[] Archive);

/// <summary>
/// One bounded slice of a retention pass: what to purge now, and where the next slice starts.
/// </summary>
/// <param name="Candidates">At most the batch size of records.</param>
/// <param name="ResumeFrom">The key the next slice starts scanning at, or empty when the scan is done.</param>
public sealed record HistoryPurgeBatch(IReadOnlyList<HistoryPurgeCandidate> Candidates, byte[] ResumeFrom)
{
	/// <summary>True when this was the last slice.</summary>
	public bool IsLast => ResumeFrom.Length == 0;
}
