using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Play activity counted from poses and scenes: per character and per room, per UTC day, with no text
/// and nothing about connection time. Written in batches by <see cref="Services.Interfaces.IActivityLedger"/>;
/// retention purges whole days by age through the store's <see cref="Services.Interfaces.IHistoryStore"/>
/// kind <c>activity</c>.
/// </summary>
public interface IActivityStore
{
	/// <summary>Adds <paramref name="batch"/> to what is kept, in one write.</summary>
	ValueTask MergeActivityAsync(ActivityBatch batch, CancellationToken cancellationToken = default);

	/// <summary>The kept days from <paramref name="fromDay"/> to <paramref name="toDay"/>, both included, and every last pose.</summary>
	ValueTask<ActivityWindow> GetActivityAsync(int fromDay, int toDay, CancellationToken cancellationToken = default);
}
