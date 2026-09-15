namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// One entry of a PennMUSH dump's <c>+FLAGS LIST</c> or <c>+POWER LIST</c> table: a site's flag or
/// power definition, the way <c>flag_write_all</c> (src/flags.c) writes it. The two tables share a
/// shape because PennMUSH stores powers in a flag table of their own.
/// </summary>
public class PennMUSHFlagDefinition
{
	/// <summary>
	/// The primary name, by which an object's <c>flags</c> or <c>powers</c> field refers to it.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// The one-character abbreviation PennMUSH lists the flag by, or the empty string for none.
	/// </summary>
	public string Letter { get; init; } = string.Empty;

	/// <summary>The object types it may be set on; empty means any.</summary>
	public List<string> Types { get; init; } = [];

	/// <summary>The permissions needed to set it — PennMUSH's <c>perms</c>.</summary>
	public List<string> SetPermissions { get; init; } = [];

	/// <summary>The permissions needed to clear it — PennMUSH's <c>negate_perms</c>.</summary>
	public List<string> UnsetPermissions { get; init; } = [];

	/// <summary>
	/// The other names it answers to, gathered from the table's alias rows. PennMUSH writes one row
	/// per alias, so a definition with two of them appears twice in that list.
	/// </summary>
	public List<string> Aliases { get; init; } = [];

	/// <summary>
	/// Whether PennMUSH keeps this definition to itself: <c>internal</c> among its permissions marks a
	/// flag no player may set, which is server state rather than site configuration (CONNECTED, GOING).
	/// </summary>
	public bool IsInternal
		=> SetPermissions.Contains("internal", StringComparer.OrdinalIgnoreCase)
			|| UnsetPermissions.Contains("internal", StringComparer.OrdinalIgnoreCase);
}
