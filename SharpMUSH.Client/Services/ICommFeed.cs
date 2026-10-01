namespace SharpMUSH.Client.Services;

/// <summary>A channel the viewer can read, with how many lines arrived since they last looked.</summary>
public sealed record CommChannel(string Name, int Unread, bool Joined = true);

/// <summary>One channel line or page, as the viewer received it.</summary>
/// <param name="Id">The line's id where it has one — a channel line from the server's buffer, pulled or
/// pushed. Two copies of a line with the same id are one line. Pages have none.</param>
public sealed record CommMessage(string Kind, string? Channel, IReadOnlyList<string> To, string From, string? FromObjId, string Text, DateTimeOffset Timestamp, long? Id = null);

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

	/// <summary>
	/// Pulls a channel's recent lines from the server and files them with what has been pushed, keeping one
	/// copy of a line known by its id. A conversation has no server history yet, and pulls nothing.
	/// </summary>
	Task LoadHistoryAsync(string key);

	/// <summary>
	/// The key the viewer is looking at, or null. Its lines don't count as unread; setting it marks the
	/// key read.
	/// </summary>
	string? Viewing { get; set; }

	event Action? Changed;
}
