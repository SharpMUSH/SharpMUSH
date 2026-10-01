namespace SharpMUSH.Library.Models;

/// <summary>
/// Where a character has read up to in one channel or page conversation: the portal's unread counts are
/// worked out from these, so they survive a reload and a change of device.
/// </summary>
/// <param name="Scope">What was read: <see cref="ReadMarkerScope.Channel"/> or <see cref="ReadMarkerScope.Conversation"/>.</param>
/// <param name="LastReadId">The id of the last line read (<see cref="SharpChannelMessage.Id"/>), or null where
/// lines have none — pages, which have no history yet.</param>
/// <param name="LastReadAt">When the last line read was sent.</param>
public sealed record SharpReadMarker(string Scope, long? LastReadId, DateTimeOffset LastReadAt)
{
	/// <summary>
	/// Whether this marker is further on than <paramref name="other"/>: by id when both have one (ids rise
	/// with time, and two lines can share a millisecond), by time otherwise.
	/// </summary>
	public bool IsPast(SharpReadMarker other) =>
		LastReadId is { } id && other.LastReadId is { } otherId
			? id > otherId
			: LastReadAt > other.LastReadAt;
}

/// <summary>The scope strings a <see cref="SharpReadMarker"/> is filed under.</summary>
public static class ReadMarkerScope
{
	private const string ChannelPrefix = "channel:";
	private const string ConversationPrefix = "page:";

	/// <summary>A channel, by its id: a marker follows the channel through a rename.</summary>
	public static string Channel(string channelId) => ChannelPrefix + channelId;

	/// <summary>
	/// A page conversation, by the other people in it: their objids, distinct and sorted, so the same
	/// people in any order are one conversation.
	/// </summary>
	public static string Conversation(IEnumerable<DBRef> others) =>
		ConversationPrefix + string.Join(' ', others.Select(other => other.ToString()).Distinct().Order(StringComparer.Ordinal));

	/// <summary>The channel id a scope names, if it names a channel.</summary>
	public static bool IsChannel(string scope, out string channelId)
	{
		var isChannel = scope.StartsWith(ChannelPrefix, StringComparison.Ordinal);
		channelId = isChannel ? scope[ChannelPrefix.Length..] : string.Empty;
		return isChannel;
	}

	/// <summary>The objids a scope names, if it names a conversation.</summary>
	public static bool IsConversation(string scope, out string[] others)
	{
		var isConversation = scope.StartsWith(ConversationPrefix, StringComparison.Ordinal);
		others = isConversation ? scope[ConversationPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
		return isConversation;
	}
}
