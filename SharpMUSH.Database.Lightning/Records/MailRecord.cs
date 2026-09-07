namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// Mirrors <c>SurrealDatabase.MailDbRecord</c>. Sender and recipient are not fields here — they are
/// the index tables (<c>Tables.MailBox</c> / <c>Tables.MailSent</c>) that map a dbref to this row's key,
/// mirroring how <c>received_mail</c> / <c>mail_sender</c> are separate edges in SurrealDB.
/// <c>Content</c> and <c>Subject</c> are <c>MModule.serialize</c> output.
/// </summary>
public sealed record MailRecord
{
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
