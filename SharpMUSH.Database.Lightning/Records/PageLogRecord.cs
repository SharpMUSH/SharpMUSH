namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// One character's copy of a logged page. The key is the character's dbref, the conversation (the others'
/// objids) and the page's id; the record carries the character's creation time, so a copy left by an
/// earlier holder of a recycled dbref is not theirs.
/// </summary>
public sealed record PageLogRecord
{
	public long CharacterCreationTime { get; init; }
	public long Id { get; init; }
	public string Sender { get; init; } = "";
	public string SenderName { get; init; } = "";
	public string[] Recipients { get; init; } = [];
	public string[] RecipientNames { get; init; } = [];
	public string Style { get; init; } = "";
	public string Message { get; init; } = "";
	public long TimestampMs { get; init; }
}

/// <summary>
/// One of a character's logged page conversations: who it is with, their names as the latest page named
/// them, and that page's id and time. Keyed by the character's dbref and the conversation.
/// </summary>
public sealed record PageConversationRecord
{
	public long CharacterCreationTime { get; init; }
	public string[] With { get; init; } = [];
	public string[] Names { get; init; } = [];
	public long LastId { get; init; }
	public long LastAtMs { get; init; }
}
