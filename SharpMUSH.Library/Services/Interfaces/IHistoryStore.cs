using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// One kind of history a store keeps — every version a record has had, beyond the one it shows now —
/// that a retention pass can measure and bound. The provider registers its own (wiki revisions); a
/// storage plugin registers its own beside them (the Scene plugin's pose edits and soft-deleted poses),
/// so <see cref="IHistoryRetentionService"/> covers whatever is installed without knowing what it is.
///
/// <para>A pass is a loop of <see cref="FindPurgeableAsync"/> (a read) and <see cref="PurgeAsync"/> (one
/// write job), so no transaction spans more than one batch. <see cref="PurgeAsync"/> re-judges every
/// candidate against the rule inside its write job, because the read that chose it is already in the
/// past: a page edited, or a pose undone, in between can make a candidate the version that must be
/// kept.</para>
/// </summary>
public interface IHistoryStore
{
	/// <summary>
	/// A short stable name — <c>wiki</c>, <c>scene.edits</c> — that the policy's settings are keyed by
	/// (see <see cref="HistoryRetentionOptions.SettingNameFor"/>).
	/// </summary>
	string Kind { get; }

	/// <summary>What the records are, for a wizard reading the report.</summary>
	string Description { get; }

	/// <summary>Counts what the live world holds. One read over the whole kind.</summary>
	ValueTask<HistoryUsage> MeasureAsync(CancellationToken ct = default);

	/// <summary>
	/// Reads the next slice of the kind starting at <paramref name="resumeFrom"/> (empty for the start)
	/// and returns at most <paramref name="limit"/> records <paramref name="rule"/> allows purging as of
	/// <paramref name="now"/>, oldest first within each stream.
	/// </summary>
	ValueTask<HistoryPurgeBatch> FindPurgeableAsync(HistoryRetentionRule rule, DateTimeOffset now, byte[] resumeFrom,
		int limit, CancellationToken ct = default);

	/// <summary>
	/// Deletes the batch's candidates that <paramref name="rule"/> still allows purging, in one write
	/// job, keeping every index that points at them consistent. Returns how many it deleted and what
	/// they occupied.
	/// </summary>
	ValueTask<(int Records, long Bytes)> PurgeAsync(HistoryPurgeBatch batch, HistoryRetentionRule rule, DateTimeOffset now,
		CancellationToken ct = default);
}
