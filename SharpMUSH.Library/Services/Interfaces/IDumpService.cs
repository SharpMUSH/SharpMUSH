using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// PennMUSH's database save, with the messages and events around it (<c>compatibility_events</c>): the warnings
/// before a scheduled save, and the save itself, which <c>@dump</c>, the <c>dump_interval</c> timer and SIGUSR2 run.
/// SharpMUSH commits every change as it is made, so the save flushes the world to disk (<see cref="IWorldSaveService"/>).
/// </summary>
public interface IDumpService
{
	/// <summary>
	/// Warns that a scheduled save is <paramref name="minutes"/> (5 or 1) away: raises <c>DUMP`5MIN</c> or
	/// <c>DUMP`1MIN</c> and shows <c>dump_warning_5min</c> or <c>dump_warning_1min</c> to every connected player.
	/// </summary>
	ValueTask WarnAsync(int minutes);

	/// <summary>
	/// Shows <c>dump_message</c>, saves, then raises <c>DUMP`COMPLETE</c> and shows <c>dump_complete</c>, or raises
	/// <c>DUMP`ERROR</c> and tells wizards and royalty that the save failed.
	/// </summary>
	/// <param name="enactor">Who asked for it (<c>@dump</c>), or null for the timer and the signal.</param>
	ValueTask<Result<Success>> DumpAsync(DBRef? enactor);
}
