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

/// <summary>
/// Who is on a channel now, as <c>@channel/who</c> lists them for the acting character: connected players
/// and things, a member hiding on the channel only to a viewer who may see them. The <c>comm.who</c> push
/// keeps it current (docs/softcode/comm-feed-handler.md).
/// </summary>
/// <param name="Channel">The channel's name.</param>
/// <param name="Members">In the order the channel holds them.</param>
public sealed record ChannelWhoList(string Channel, IReadOnlyList<ChannelWhoMember> Members);

/// <summary>One member <c>@channel/who</c> lists.</summary>
public sealed record ChannelWhoMember(string Name, string Objid);

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
/// The last page of a conversation the character has read: by the page's id (the one its
/// <c>comm.message</c> carries), and its time. A marker written before pages had ids has a null
/// <see cref="LastReadId"/>, and is compared by time until a marker with an id replaces it.
/// </summary>
/// <param name="With">The other people in it, by objid, sorted.</param>
public sealed record ConversationReadMarker(IReadOnlyList<string> With, long? LastReadId, DateTimeOffset LastReadAt);

/// <summary>Moves a channel's marker on. A marker never moves back: an update behind it is ignored.</summary>
public sealed record ReadMarkerUpdate(long? LastReadId, DateTimeOffset LastReadAt);

/// <summary>Moves a conversation's marker on. <see cref="With"/> is the other people by objid, in any order;
/// the acting character's own objid among them is ignored.</summary>
public sealed record ConversationReadMarkerUpdate(IReadOnlyList<string> With, long? LastReadId, DateTimeOffset LastReadAt);

/// <summary>Limits the portal and the server share about page conversations.</summary>
public static class CommLimits
{
	/// <summary>
	/// The most other people one page conversation holds. The read-marker and recall endpoints refuse a
	/// larger one, a page to more is not logged, and the portal does not mark one.
	/// </summary>
	public const int ConversationMaxOthers = 32;

	/// <summary>
	/// The most logged pages one recall returns: the portal's conversation recall, <c>page/recall</c> and
	/// <c>pagerecall()</c> alike.
	/// </summary>
	public const int PageRecallMaxLines = 500;

	/// <summary>The most page conversations one listing returns, read without reading the rest.</summary>
	public const int PageConversationListMax = 100;
}

/// <summary>
/// One logged page, in the shape of the <c>comm.message</c> push for the same page
/// (docs/softcode/comm-feed-handler.md), so the portal files a pulled page exactly as a pushed one.
/// </summary>
/// <param name="Id">The page's id: the <c>id</c> its <c>comm.message</c> carried, from the sequence channel
/// lines take theirs from.</param>
/// <param name="To">The recipients' names, in the order paged.</param>
/// <param name="ToObjids">The recipients' objids, lined up with <paramref name="To"/>.</param>
/// <param name="From">The pager's name as the page named them.</param>
/// <param name="FromObjid">The pager's objid.</param>
/// <param name="Text">The page as the terminal reads it after the page prefix: a pose carries the name.</param>
/// <param name="Style"><c>say</c>, <c>pose</c> or <c>semipose</c>.</param>
/// <param name="Ts">Milliseconds since 1970.</param>
public sealed record PageRecallLine(
	long Id,
	IReadOnlyList<string> To,
	IReadOnlyList<string> ToObjids,
	string From,
	string FromObjid,
	string Text,
	string Style,
	long Ts);

/// <summary>
/// A page conversation's logged history, as the acting character's own copy.
/// </summary>
/// <param name="Logging">Whether the game keeps a page log (<c>page_log</c>). When it does not, there are no
/// lines, and the portal says so rather than showing an empty history as if nobody had paged.</param>
/// <param name="Lines">The last lines asked for, oldest first.</param>
public sealed record PageRecall(bool Logging, IReadOnlyList<PageRecallLine> Lines);

/// <summary>One of the acting character's logged page conversations.</summary>
/// <param name="With">The others in it, by objid, sorted (the character alone, for pages to themselves).
/// Joined with spaces, this is the conversation's key in <c>api/comm/conversations/{key}/recall</c>.</param>
/// <param name="Names">Their names as the latest page named them, lined up with <paramref name="With"/>.</param>
/// <param name="LastId">The latest page's id.</param>
/// <param name="LastAt">When the latest page was sent.</param>
public sealed record PageConversationSummary(
	IReadOnlyList<string> With,
	IReadOnlyList<string> Names,
	long LastId,
	DateTimeOffset LastAt);

/// <summary>The acting character's logged page conversations.</summary>
/// <param name="Character">The objid of the character these are for. A client holding a different
/// character's feed ignores them.</param>
/// <param name="Logging">Whether the game keeps a page log (<c>page_log</c>); when it does not, the list is
/// empty.</param>
public sealed record PageConversations(string Character, bool Logging, IReadOnlyList<PageConversationSummary> Conversations);
