using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Notifications;

public record ChannelMessageNotification(
	SharpChannel Channel,
	AnyOptionalSharpObject Source,
	INotifyService.NotificationType MessageType,
	MString Message,
	MString Title,
	MString PlayerName,
	MString Says,
	string[] Options, // silent or noisy
	/// <summary>
	/// PennMUSH's <c>CB_SEEALL</c> (hdrs/extchat.h:87): deliver only to channel members who are
	/// See_All, plus the source itself. <c>chat_player_announce</c> (src/extchat.c:3190) sets it for
	/// a connect/disconnect line from a hidden connection, or from a member hidden on that channel,
	/// so a hidden player's arrival does not leak to the rest of the channel. The line is still
	/// buffered, tagged, and filtered again on recall (src/extchat.c:3559,4083).
	/// </summary>
	bool SeeAllOnly = false
	) : INotification;
