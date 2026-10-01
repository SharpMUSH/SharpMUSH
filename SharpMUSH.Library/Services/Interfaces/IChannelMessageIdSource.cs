namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Hands out the id each channel line carries (<see cref="Models.SharpChannelMessage.Id"/>): in the recall
/// buffer, in the <c>CHANNEL`MESSAGE</c> event and so in the <c>comm.message</c> push, and from the portal's
/// recall endpoint. Pages take theirs from the same sequence (<see cref="Models.SharpPage.Id"/>, through
/// <see cref="IPageLogService"/>), so a channel line and a page never share an id.
/// </summary>
public interface IChannelMessageIdSource
{
	/// <summary>The next id: larger than every id handed out before it, here and before a restart.</summary>
	ValueTask<long> NextAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// The largest id handed out so far by this process, or 0. Ids can run ahead of the clock (a stopped
	/// clock, or one set back), so this, not the time, is the bound on an id a client may have seen.
	/// </summary>
	long Latest { get; }
}
