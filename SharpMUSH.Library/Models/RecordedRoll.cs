namespace SharpMUSH.Library.Models;

/// <summary>
/// A roll to keep: who rolled it, through which object, and the result as roll() reports it.
/// </summary>
/// <param name="At">When it was rolled.</param>
/// <param name="RollerObjid">The player or thing the roll was made for (the enactor).</param>
/// <param name="RollerName">Its name when it rolled.</param>
/// <param name="ObjectObjid">The object whose code rolled it (the executor), when that is not the roller.</param>
/// <param name="Formula">The formula as given.</param>
/// <param name="Total">The total.</param>
/// <param name="Text">The one-line account, <c>2d6[3, 4] + 5 = 12</c>.</param>
/// <param name="Json">The result as roll()'s json output writes it.</param>
public sealed record RollDraft(DateTimeOffset At, string RollerObjid, string RollerName, string? ObjectObjid,
	string Formula, decimal Total, string Text, string Json);

/// <summary>A kept roll, under the id <c>rollinfo()</c> reads it by.</summary>
public sealed record RecordedRoll(long Id, RollDraft Roll);
