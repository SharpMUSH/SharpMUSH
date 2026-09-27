using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Delivers one channel line to a channel's members: PennMUSH's <c>channel_send</c>
/// (<c>src/extchat.c</c>). Runs the channel's <c>MOGRIFY`*</c> chain, builds the default line, applies
/// each member's own <c>@chatformat</c>, and adds the line to the channel's recall buffer.
/// </summary>
public interface IChannelBroadcastService
{
	/// <summary>Sends <paramref name="notification"/> to every member of its channel who may hear it.</summary>
	ValueTask BroadcastAsync(ChannelMessageNotification notification, CancellationToken cancellationToken);
}
