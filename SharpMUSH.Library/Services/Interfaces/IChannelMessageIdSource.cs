namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Hands out the id each channel line carries (<see cref="Models.SharpChannelMessage.Id"/>): in the recall
/// buffer, in the <c>CHANNEL`MESSAGE</c> event and so in the <c>comm.message</c> push, and from the portal's
/// recall endpoint.
/// </summary>
public interface IChannelMessageIdSource
{
	/// <summary>The next id: larger than every id handed out before it, here and before a restart.</summary>
	long Next();
}
