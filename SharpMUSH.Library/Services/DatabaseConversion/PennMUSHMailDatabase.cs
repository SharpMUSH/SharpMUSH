namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// What the importer reads from a PennMUSH maildb (<c>load_mail</c>, <c>src/extmail.c</c>): its flags
/// line, its mail aliases (<c>load_malias</c>, <c>src/malias.c</c>) and its messages.
/// </summary>
public class PennMUSHMailDatabase
{
	/// <summary><c>MDBF_SUBJECT</c> (<c>hdrs/extmail.h</c>): each message carries its subject.</summary>
	public const int SubjectFlag = 0x1;

	/// <summary><c>MDBF_ALIASES</c> (<c>hdrs/extmail.h</c>): the file carries an alias section.</summary>
	public const int AliasesFlag = 0x2;

	/// <summary><c>MDBF_SENDERCTIME</c> (<c>hdrs/extmail.h</c>): each message carries its sender's creation time.</summary>
	public const int SenderCreationTimeFlag = 0x8;

	/// <summary>The <c>MDBF_*</c> bits from the leading <c>+N</c> line; 0 for a file without one.</summary>
	public int Flags { get; set; }

	/// <summary>The alias section, in file order.</summary>
	public List<PennMUSHMailAlias> Aliases { get; set; } = [];

	/// <summary>
	/// <c>mdb_top</c>, the message count <c>dump_mail</c> writes after the aliases; <c>null</c> when that
	/// line is missing or unreadable.
	/// </summary>
	public int? MessageCount { get; set; } = 0;

	/// <summary>The messages, in file order, which within one recipient is the order PennMUSH lists them in.</summary>
	public List<PennMUSHMailMessage> Messages { get; set; } = [];

	/// <summary>
	/// Why reading the messages stopped short of <see cref="MessageCount"/>; <c>null</c> when every one was
	/// read. The aliases and the messages before the fault are kept.
	/// </summary>
	public string? MessageReadError { get; set; }

	/// <summary>Why the maildb could not be read; <c>null</c> when it was. A maildb that fails leaves the rest empty.</summary>
	public string? ReadError { get; set; }
}

/// <summary>
/// One <c>struct mail_alias</c> as <c>save_malias</c> writes it: owner, name (no <c>+</c>), description,
/// use and see privilege bits, and member dbrefs.
/// </summary>
public record PennMUSHMailAlias(
	int Owner,
	string Name,
	string Description,
	int UsePrivileges,
	int SeePrivileges,
	List<int> Members);

/// <summary>
/// One <c>struct mail</c> as <c>dump_mail</c> writes it: recipient, sender, the sender's creation time
/// (0 when the file predates <c>MDBF_SENDERCTIME</c>), the time sent as <c>show_time</c> prints it in the
/// game's local time, subject, body, and the <c>read</c> word of <c>M_*</c> bits with the folder in
/// bits 8-11.
/// </summary>
public record PennMUSHMailMessage(
	int To,
	int From,
	long FromCreationTime,
	string Time,
	string Subject,
	string Body,
	int Flags)
{
	/// <summary><c>M_MSGREAD</c>.</summary>
	public bool IsRead => (Flags & 0x1) != 0;

	/// <summary><c>M_CLEARED</c>.</summary>
	public bool IsCleared => (Flags & 0x2) != 0;

	/// <summary><c>M_URGENT</c>.</summary>
	public bool IsUrgent => (Flags & 0x4) != 0;

	/// <summary><c>M_TAG</c>.</summary>
	public bool IsTagged => (Flags & 0x40) != 0;

	/// <summary><c>M_FORWARD</c>.</summary>
	public bool IsForwarded => (Flags & 0x80) != 0;

	/// <summary><c>Folder(m)</c>: <c>(read &amp; ~M_FMASK) &gt;&gt; 8</c>.</summary>
	public int Folder => (Flags & 0x0F00) >> 8;
}
