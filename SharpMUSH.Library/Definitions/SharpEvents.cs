namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Names of SharpMUSH-fired events (attributes evaluated on the configured event_handler object).
/// PennMUSH-parity events are fired with string literals at their call sites; this holds
/// SharpMUSH extension events so the name has a single definition shared by firer and tests.
/// </summary>
public static class SharpEvents
{
	/// <summary>
	/// Fired (room-scoped, not actor-scoped) whenever a room's visible contents change — an
	/// object enters/leaves, or a player connects/disconnects in it. Args: (roomobjid, cause).
	/// The handler is expected to fan out structured pushes to that room's connected occupants.
	/// Cause <c>resume</c> is a web session the connection server rebound (a reload): nothing in the
	/// room changed, and the enactor, the resuming player, is the one to send the room to again.
	/// </summary>
	public const string RoomContents = "ROOM`CONTENTS";

	/// <summary>
	/// Fired once per channel line, after it has been delivered, naming exactly the members who were
	/// sent it (a gagged member, one whose <c>@chatformat</c> silenced the line, one the speaker may not
	/// be heard by, and a mute member's presence line are all left out). A presence line is sent only on a
	/// channel with the <c>Announce</c> privilege. Args: (channel name, speaker
	/// objid or empty, style, speaker name, message, recipient objids, unix-ms, line id). The line id is
	/// the one the recall buffer holds the line under, and rises with time. The style is
	/// <c>say</c>, <c>pose</c>, <c>semipose</c>, <c>emit</c> or <c>presence</c>; the name and message are
	/// plain text, after the channel's mogrifier.
	/// </summary>
	public const string ChannelMessage = "CHANNEL`MESSAGE";

	/// <summary>
	/// Fired once per page that reached at least one recipient. Args: (pager objid, recipient objids,
	/// style, pager name, message, unix-ms, page id). Only recipients who were paged are named: one who is
	/// not connected, is HAVEN, or whose page lock refuses the pager is not. The style is <c>say</c>,
	/// <c>pose</c> or <c>semipose</c>; the message is plain text. The id comes from the sequence channel
	/// line ids come from, and is the one the page log (<c>page_log</c>) keeps the page under.
	/// </summary>
	public const string PageMessage = "PAGE`MESSAGE";

	/// <summary>
	/// Fired (player-scoped) when a connected player's channel list may have changed: on connect, on
	/// joining or leaving a channel, on a change to their own channel flags, and when one of their
	/// channels is renamed or deleted. Args: (player objid, cause, channel name or empty). Cause is
	/// <c>connect</c>, <c>resume</c> (a web session the connection server rebound, sent its list again),
	/// <c>join</c>, <c>leave</c>, <c>status</c>, <c>rename</c> or <c>delete</c>.
	/// </summary>
	public const string PlayerChannels = "PLAYER`CHANNELS";

	/// <summary>
	/// Fired when a member comes onto or goes off a channel's member list, as <c>@channel/who</c> lists it
	/// (a thing always, a player while connected, a member hiding on the channel only to a viewer with
	/// <c>Priv_Who</c>), naming exactly the connected player members whose view of the list changed. Args:
	/// (channel name, member objid, member name, <c>on</c> or <c>off</c>, viewer objids, cause). Cause is
	/// <c>connect</c>, <c>disconnect</c>, <c>join</c>, <c>leave</c> or <c>status</c> (the member hid or
	/// stopped hiding). One change fires at most twice: once for the viewers who now list the member, once for
	/// those who no longer do.
	/// </summary>
	public const string ChannelWho = "CHANNEL`WHO";
}
