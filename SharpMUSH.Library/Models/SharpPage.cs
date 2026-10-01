namespace SharpMUSH.Library.Models;

/// <summary>
/// One delivered page as the page log keeps it (the <c>page_log</c> option): the parts the portal needs to
/// show it exactly as the <c>comm.message</c> push for the same page, which carries the same
/// <see cref="Id"/>.
/// </summary>
/// <param name="Id">The page's id, from the same sequence as channel lines
/// (<see cref="Services.Interfaces.IChannelMessageIdSource"/>): it rises with time, also across a restart.</param>
/// <param name="Sender">The pager, by objid.</param>
/// <param name="SenderName">The pager's name as the page named them (with the page alias when
/// <c>page_aliases</c> is on).</param>
/// <param name="Recipients">The players the page reached, by objid, in the order paged. One who refused it
/// is not here.</param>
/// <param name="RecipientNames">Their names when paged, in the same order.</param>
/// <param name="Style"><c>say</c>, <c>pose</c> or <c>semipose</c>.</param>
/// <param name="Message">The message, plain, without the pose token.</param>
/// <param name="Timestamp">When it was sent.</param>
public sealed record SharpPage(
	long Id,
	DBRef Sender,
	string SenderName,
	IReadOnlyList<DBRef> Recipients,
	IReadOnlyList<string> RecipientNames,
	string Style,
	string Message,
	DateTimeOffset Timestamp)
{
	/// <summary>Everyone in the page, sender first, each once.</summary>
	public IReadOnlyList<DBRef> Participants => Recipients.Prepend(Sender).Distinct().ToArray();

	/// <summary>
	/// The conversation this page is in, as <paramref name="owner"/> sees it: everyone else in it, sorted
	/// (<see cref="PageConversation.Normalize"/>). A page to oneself is a conversation with oneself.
	/// </summary>
	public IReadOnlyList<DBRef> ConversationFor(DBRef owner)
	{
		var others = Participants.Where(participant => participant != owner).ToArray();
		return others.Length > 0 ? PageConversation.Normalize(others) : [owner];
	}

	/// <summary>The name the page gave <paramref name="participant"/>: the sender's, or a recipient's.</summary>
	public string NameOf(DBRef participant)
	{
		if (participant == Sender) return SenderName;

		for (var i = 0; i < Recipients.Count && i < RecipientNames.Count; i++)
		{
			if (Recipients[i] == participant) return RecipientNames[i];
		}

		return participant.ToString();
	}
}

/// <summary>One of a character's logged page conversations: who it is with and when it last spoke.</summary>
/// <param name="With">The others in it, by objid, sorted as <see cref="PageConversation.Normalize"/> sorts
/// them (the character alone, for pages to themselves).</param>
/// <param name="Names">Their names as the latest page named them, in the order of <paramref name="With"/>.</param>
/// <param name="LastId">The latest page's id.</param>
/// <param name="LastAt">When the latest page was sent.</param>
public sealed record SharpPageConversation(
	IReadOnlyList<DBRef> With,
	IReadOnlyList<string> Names,
	long LastId,
	DateTimeOffset LastAt);

/// <summary>How a page conversation is named: the same people in any order are one conversation.</summary>
public static class PageConversation
{
	/// <summary>The people, each once, sorted by objid ordinally — the order <see cref="ReadMarkerScope.Conversation"/> uses.</summary>
	public static IReadOnlyList<DBRef> Normalize(IEnumerable<DBRef> people) =>
		people.Distinct().OrderBy(person => person.ToString(), StringComparer.Ordinal).ToArray();

	/// <summary>The conversation's key: the people's objids, normalized, space-separated.</summary>
	public static string Key(IEnumerable<DBRef> people) => string.Join(' ', Normalize(people));
}
