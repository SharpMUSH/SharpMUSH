namespace SharpMUSH.Client.Services;

/// <summary>
/// A channel the viewer may see, with how many lines arrived since they last looked. One they are not on
/// (<paramref name="Joined"/> false) is there for the channel browser alone.
/// </summary>
/// <param name="Gagged">They are on it with it gagged: still on, hearing nothing.</param>
/// <param name="Members">Everyone on it, connected or not, when the game said.</param>
/// <param name="Description">Its <c>@channel/describe</c> text, empty when it has none or the game did not say.</param>
public sealed record CommChannel(string Name, int Unread, bool Joined = true, bool Gagged = false, int? Members = null,
	string Description = "");

/// <summary>One channel line or page, as the viewer received it.</summary>
/// <param name="Id">The line's id where it has one: a channel line or a page, pulled from the server or
/// pushed. Two copies of a line with the same id are one line. A game whose comm-feed package predates ids
/// sends none.</param>
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
	/// Pulls a channel's recent lines, or a conversation's logged pages, from the server and files them with
	/// what has been pushed, keeping one copy of a line known by its id.
	/// </summary>
	Task LoadHistoryAsync(string key);

	/// <summary>
	/// Whether the game keeps a page log (<c>page_log</c>), as the server last said; null until it has. With
	/// it off a conversation holds only the pages that arrived since the portal loaded.
	/// </summary>
	bool? PageLogging { get; }

	/// <summary>
	/// The key the viewer is looking at, or null. Its lines don't count as unread; setting it marks the
	/// key read.
	/// </summary>
	string? Viewing { get; set; }

	event Action? Changed;
}
