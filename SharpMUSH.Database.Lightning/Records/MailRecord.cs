namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// Mirrors <c>SurrealDatabase.MailDbRecord</c>, plus <see cref="Sender"/> and <see cref="Recipient"/>
/// so a mail row is the source of truth for both ends of the exchange; <c>Tables.MailBox</c> and
/// <c>Tables.MailSent</c> are lookup indexes only (recipient/sender + mail id -> this row's key),
/// mirroring how <c>received_mail</c> / <c>mail_sender</c> are separate edges in SurrealDB.
/// <c>Content</c> and <c>Subject</c> are <c>MarkupTextSerializer.Serialize</c> output.
/// </summary>
public sealed record MailRecord
{
	public long Sender { get; init; }
	public long Recipient { get; init; }
	public long DateSent { get; init; }
	public bool Fresh { get; init; }
	public bool Read { get; init; }
	public bool Tagged { get; init; }
	public bool Urgent { get; init; }
	public bool Forwarded { get; init; }
	public bool Cleared { get; init; }
	public string Folder { get; init; } = "";
	public string Content { get; init; } = "";
	public string Subject { get; init; } = "";
}
