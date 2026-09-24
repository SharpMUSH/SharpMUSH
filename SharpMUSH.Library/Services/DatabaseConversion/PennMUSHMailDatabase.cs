namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// What the importer reads from a PennMUSH maildb (<c>load_mail</c>, <c>src/extmail.c</c>): its flags
/// line and its mail aliases (<c>load_malias</c>, <c>src/malias.c</c>).
/// </summary>
public class PennMUSHMailDatabase
{
	/// <summary><c>MDBF_ALIASES</c> (<c>hdrs/extmail.h</c>): the file carries an alias section.</summary>
	public const int AliasesFlag = 0x2;

	/// <summary>The <c>MDBF_*</c> bits from the leading <c>+N</c> line; 0 for a file without one.</summary>
	public int Flags { get; set; }

	/// <summary>The alias section, in file order.</summary>
	public List<PennMUSHMailAlias> Aliases { get; set; } = [];
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
