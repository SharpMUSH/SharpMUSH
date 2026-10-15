namespace SharpMUSH.Library.Definitions;

/// <summary>
/// The names of the events a SharpMUSH game raises, one constant each, so the raiser, the catalogue
/// (<see cref="EventCatalogue"/>) and the tests share one spelling. Every raise names its event through one of
/// these; a test fails when a raise does not, or names an event the catalogue lacks.
/// </summary>
public static class SharpEvents
{
	// PennMUSH's events, by the names PennMUSH gives them (help event list).
	public const string DbPurge = "DB`PURGE";
	public const string DbWcheck = "DB`WCHECK";
	public const string LogErr = "LOG`ERR";
	public const string LogCmd = "LOG`CMD";
	public const string LogWiz = "LOG`WIZ";
	public const string LogConn = "LOG`CONN";
	public const string LogTrace = "LOG`TRACE";
	public const string LogCheck = "LOG`CHECK";
	public const string ObjectCreate = "OBJECT`CREATE";
	public const string ObjectDestroy = "OBJECT`DESTROY";
	public const string ObjectMove = "OBJECT`MOVE";
	public const string ObjectRename = "OBJECT`RENAME";
	public const string ObjectFlag = "OBJECT`FLAG";
	public const string PlayerCreate = "PLAYER`CREATE";
	public const string PlayerConnect = "PLAYER`CONNECT";
	public const string PlayerDisconnect = "PLAYER`DISCONNECT";
	public const string PlayerWarn = "PLAYER`WARN";
	public const string PlayerLink = "PLAYER`LINK";
	public const string PlayerUnlink = "PLAYER`UNLINK";
	public const string SocketConnect = "SOCKET`CONNECT";
	public const string SocketDisconnect = "SOCKET`DISCONNECT";
	public const string SocketLoginFail = "SOCKET`LOGINFAIL";
	public const string SocketCreateFail = "SOCKET`CREATEFAIL";
	public const string SocketGmcp = "SOCKET`GMCP";
	public const string SocketTimeZone = "SOCKET`TIMEZONE";
	public const string HttpBlocked = "HTTP`BLOCKED";
	public const string HttpCommand = "HTTP`COMMAND";

	public const string Dump5Min = "DUMP`5MIN";
	public const string Dump1Min = "DUMP`1MIN";
	public const string DumpComplete = "DUMP`COMPLETE";
	public const string DumpError = "DUMP`ERROR";
	public const string DbDbck = "DB`DBCK";
	public const string LogHuh = "LOG`HUH";
	public const string SqlConnect = "SQL`CONNECT";
	public const string SqlConnectFail = "SQL`CONNECTFAIL";
	public const string SqlDisconnect = "SQL`DISCONNECT";
	public const string SignalUsr1 = "SIGNAL`USR1";
	public const string SignalUsr2 = "SIGNAL`USR2";
	public const string PlayerInactivity = "PLAYER`INACTIVITY";
	public const string HttpFail = "HTTP`FAIL";

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
	/// be heard by, and a mute member's presence line are all left out). Args: (channel name, speaker
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

	/// <summary>A player received @mail. Args: (recipient objid, sender objid, message number, subject, urgent).</summary>
	public const string MailReceived = "MAIL`RECEIVED";

	/// <summary>A copy of the world was written by @backup or the backup schedule. Args: (name, bytes).</summary>
	public const string BackupComplete = "BACKUP`COMPLETE";

	/// <summary>A copy of the world could not be written. Args: (reason).</summary>
	public const string BackupError = "BACKUP`ERROR";

	/// <summary>
	/// Fired after an image is added to a character's profile gallery on the web portal. Args: (character
	/// objid, asset id, site-relative url, use). The enactor is the uploader; use is <c>gallery</c>,
	/// <c>avatar</c> or <c>banner</c>.
	/// </summary>
	public const string GalleryAdd = "GALLERY`ADD";

	/// <summary>
	/// Fired when someone signed in to the web portal reports a gallery image. Args: (character objid, asset
	/// id, site-relative url, reason, caption). The enactor is the reporter; the reason is their own text.
	/// </summary>
	public const string GalleryReport = "GALLERY`REPORT";

	/// <summary>
	/// Fired by the Scene plugin when a scene is made or its status changes: <c>@scene/create</c>,
	/// <c>scenecreate()</c>, and <c>@scene/set</c> or <c>sceneset()</c> of a new status. Args: (scene id, status,
	/// previous status, empty for a scene just made). The enactor is whoever caused it.
	/// </summary>
	public const string SceneStatus = "SCENE`STATUS";

	/// <summary>
	/// Fired by the Scene plugin when <c>@scene/set</c> or <c>sceneset()</c> changes a key of a scene other than its
	/// status: its time, title, room, a detail. Args: (scene id, key, its value before). The enactor is whoever changed it.
	/// </summary>
	public const string SceneChange = "SCENE`CHANGE";
}
