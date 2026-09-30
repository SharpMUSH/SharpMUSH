namespace SharpMUSH.Client.Services;

/// <summary>A channel the viewer can read, with how many lines arrived since they last looked.</summary>
public sealed record CommChannel(string Name, int Unread, bool Joined = true);

/// <summary>One channel line or page, as the viewer received it.</summary>
public sealed record CommMessage(string Kind, string? Channel, IReadOnlyList<string> To, string From, string? FromObjId, string Text, DateTimeOffset Timestamp);

/// <summary>A page conversation: the other people in it (one, or several for a group page).</summary>
public sealed record CommConversation(string Key, IReadOnlyList<string> With, IReadOnlyList<string?> WithObjIds, int Unread, DateTimeOffset LastAt);

/// <summary>
/// Channels and pages for the Play sidebar and the channel view (README §5.1, §7.3). Keys are a
/// channel's name, or a conversation's <see cref="CommConversation.Key"/>.
/// </summary>
public interface ICommFeed
{
	IReadOnlyList<CommChannel> Channels { get; }
	IReadOnlyList<CommConversation> Conversations { get; }
	IReadOnlyList<CommMessage> Messages(string key);
	void MarkRead(string key);
	event Action? Changed;
}
