using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Recorded rolls: every roll made with <c>@roll</c> or roll()'s <c>record</c> option, under an id
/// that is never handed out twice. Append-only apart from retention, which purges by age through the
/// store's <see cref="Services.Interfaces.IHistoryStore"/> kind <c>rolls</c>.
/// </summary>
public interface IRollStore
{
	/// <summary>Keeps <paramref name="draft"/> and returns it with its id.</summary>
	ValueTask<RecordedRoll> RecordRollAsync(RollDraft draft, CancellationToken cancellationToken = default);

	/// <summary>The roll kept under <paramref name="id"/>, or <see cref="NotFound"/> if there is none or it was purged.</summary>
	ValueTask<Found<RecordedRoll>> GetRollAsync(long id, CancellationToken cancellationToken = default);
}
