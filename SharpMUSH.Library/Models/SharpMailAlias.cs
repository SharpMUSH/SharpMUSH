namespace SharpMUSH.Library.Models;

/// <summary>
/// Who may use or see a mail alias: PennMUSH's <c>ALIAS_*</c> bits (<c>hdrs/malias.h</c>), with the same
/// values, so a maildb's <c>nflags</c>/<c>mflags</c> read straight in. No bit set means everyone.
/// </summary>
[Flags]
public enum MailAliasPrivileges
{
	Everyone = 0,
	Members = 0x1,
	Admin = 0x2,
	Owner = 0x4
}

/// <summary>
/// A global mail alias (PennMUSH <c>struct mail_alias</c>, <c>src/malias.c</c>): mail sent to
/// <c>+name</c> goes to every member.
/// </summary>
/// <param name="Name">The name without its <c>+</c> token; unique case-insensitively.</param>
/// <param name="Description">Plain text, as PennMUSH stores it.</param>
/// <param name="Owner">The owning player's dbref number.</param>
/// <param name="Members">Member players' dbref numbers, in the order they were added.</param>
/// <param name="UsePrivileges">PennMUSH <c>nflags</c>: who may see the alias's name and mail it.</param>
/// <param name="SeePrivileges">PennMUSH <c>mflags</c>: who may see the alias's members.</param>
public sealed record SharpMailAlias(
	string Name,
	string Description,
	int Owner,
	int[] Members,
	MailAliasPrivileges UsePrivileges,
	MailAliasPrivileges SeePrivileges);
