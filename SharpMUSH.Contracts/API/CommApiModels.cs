namespace SharpMUSH.Library.API;

// api/comm: a channel's recall buffer and the acting character's read markers, for the portal's channel
// view and its unread counts. The engine's side is CommController; the portal's is CommHistoryService.

/// <summary>
/// One line of a channel's recall buffer, in the shape of the <c>comm.message</c> push for the same line
/// (docs/softcode/comm-feed-handler.md), so the portal files a pulled line exactly as a pushed one.
/// </summary>
/// <param name="Id">The line's id: the <c>id</c> its <c>comm.message</c> carried. Ids rise with time and
/// survive a restart, so the larger of two ids is the later line.</param>
/// <param name="Channel">The channel's name.</param>
/// <param name="From">The speaker's name, after the mogrifier; empty for an <c>@cemit</c>.</param>
/// <param name="FromObjid">The speaker's objid; null for an <c>@cemit</c>.</param>
/// <param name="Text">The line after the channel name: a pose carries the name, as <c>comm.message</c>'s does.</param>
/// <param name="Style"><c>say</c>, <c>pose</c>, <c>semipose</c>, <c>emit</c> or <c>presence</c>.</param>
/// <param name="Ts">Milliseconds since 1970.</param>
public sealed record ChannelRecallLine(long Id, string Channel, string From, string? FromObjid, string Text, string Style, long Ts);

/// <summary>Where the acting character has read up to, on every channel they are on and every conversation they marked.</summary>
/// <param name="Character">The objid of the character these are for. A client holding a different character's
/// feed ignores them.</param>
public sealed record CommReadMarkers(
	string Character,
	IReadOnlyList<ChannelReadMarker> Channels,
	IReadOnlyList<ConversationReadMarker> Conversations);

/// <summary>The last line of a channel the character has read.</summary>
public sealed record ChannelReadMarker(string Channel, long? LastReadId, DateTimeOffset LastReadAt);

/// <summary>
/// The last page of a conversation the character has read. Pages have no ids yet (there is no page
/// history), so <see cref="LastReadId"/> is null and <see cref="LastReadAt"/> is what counts.
/// </summary>
/// <param name="With">The other people in it, by objid, sorted.</param>
public sealed record ConversationReadMarker(IReadOnlyList<string> With, long? LastReadId, DateTimeOffset LastReadAt);

/// <summary>Moves a channel's marker on. A marker never moves back: an update behind it is ignored.</summary>
public sealed record ReadMarkerUpdate(long? LastReadId, DateTimeOffset LastReadAt);

/// <summary>Moves a conversation's marker on. <see cref="With"/> is the other people by objid, in any order;
/// the acting character's own objid among them is ignored.</summary>
public sealed record ConversationReadMarkerUpdate(IReadOnlyList<string> With, long? LastReadId, DateTimeOffset LastReadAt);
