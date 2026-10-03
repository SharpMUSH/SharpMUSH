using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Measures and bounds the version histories the world keeps, across every registered
/// <see cref="IHistoryStore"/>. Behind <c>@storage/history</c>, <c>@storage/purge</c> and the scheduled
/// pass.
/// </summary>
public interface IHistoryRetentionService
{
	/// <summary>The policy in force.</summary>
	HistoryRetentionOptions Options { get; }

	/// <summary>The kinds of history installed, by <see cref="IHistoryStore.Kind"/>.</summary>
	IReadOnlyList<string> Kinds { get; }

	/// <summary>What each kind holds now.</summary>
	ValueTask<IReadOnlyList<HistoryUsage>> MeasureAsync(CancellationToken ct = default);

	/// <summary>
	/// Runs one retention pass over every kind under its rule: bounded batches, each archived (when an
	/// archive path is set) and then deleted in its own write transaction. One pass at a time; a second
	/// caller waits for the first.
	/// </summary>
	ValueTask<IReadOnlyList<HistoryPurgeOutcome>> PurgeAsync(CancellationToken ct = default);
}
