namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// What the importer reads from a PennMUSH chatdb (<c>load_chatdb</c>, <c>src/extchat.c</c>): its flags,
/// its save time and its channels, each with its members.
/// </summary>
public class PennMUSHChatDatabase
{
	/// <summary><c>CDB_SPIFFY</c> (<c>hdrs/extchat.h</c>): each channel carries a buffer size and a mogrifier.</summary>
	public const int SpiffyFlag = 0x1;

	/// <summary>The bits of the leading <c>+V</c> line.</summary>
	public int Flags { get; set; }

	/// <summary>The <c>savedtime</c> line; PennMUSH warns when it differs from the dump's.</summary>
	public string? SavedTime { get; set; }

	/// <summary>The channels, in file order.</summary>
	public List<PennMUSHChannel> Channels { get; set; } = [];

	/// <summary>Why the chatdb could not be read; <c>null</c> when it was. A chatdb that fails leaves the rest empty.</summary>
	public string? ReadError { get; set; }
}

/// <summary>
/// One <c>CHAN</c> as <c>save_channel</c> writes it. <see cref="Flags"/> are the <c>CHANNEL_*</c> bits;
/// <see cref="Locks"/> maps <c>join</c>, <c>speak</c>, <c>modify</c>, <c>see</c> and <c>hide</c> to the
/// key <c>unparse_boolexp</c> wrote, <c>*UNLOCKED*</c> for none.
/// </summary>
public record PennMUSHChannel(
	string Name,
	string Description,
	int Flags,
	int Creator,
	int Cost,
	int Buffer,
	int Mogrifier,
	Dictionary<string, string> Locks,
	List<PennMUSHChannelUser> Users);

/// <summary>One <c>CHANUSER</c>: the member, its <c>CU_*</c> bits and its title.</summary>
public record PennMUSHChannelUser(int DBRef, int Flags, string Title);
