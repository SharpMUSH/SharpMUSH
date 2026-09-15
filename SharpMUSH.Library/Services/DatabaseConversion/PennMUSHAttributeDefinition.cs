namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// One entry of a PennMUSH dump's <c>+ATTRIBUTES LIST</c> table: a standard attribute, its default
/// flags, who defined it and the default value PennMUSH hands out for it (<c>attr_write_all</c>,
/// src/attrib.c).
/// </summary>
public class PennMUSHAttributeDefinition
{
	/// <summary>The attribute name the definition applies to, uppercase in every dump seen.</summary>
	public required string Name { get; init; }

	/// <summary>The flags an attribute of this name is created with.</summary>
	public List<string> Flags { get; init; } = [];

	/// <summary>Who defined it, or none for PennMUSH's own <c>#-1</c>.</summary>
	public int? Creator { get; init; }

	/// <summary>
	/// The default value PennMUSH answers a read of this attribute with when no object sets it.
	/// Empty in a stock table.
	/// </summary>
	public string Data { get; init; } = string.Empty;

	/// <summary>
	/// The other names it answers to, from the table's alias rows (DESC for DESCRIBE, and so on).
	/// </summary>
	public List<string> Aliases { get; init; } = [];

	/// <summary>
	/// Whether PennMUSH keeps this definition to itself. The one such entry in a stock table is
	/// XYXXY, the password slot the parser lifts into <see cref="PennMUSHObject.Password"/>.
	/// </summary>
	public bool IsInternal => Flags.Contains("internal", StringComparer.OrdinalIgnoreCase);
}
