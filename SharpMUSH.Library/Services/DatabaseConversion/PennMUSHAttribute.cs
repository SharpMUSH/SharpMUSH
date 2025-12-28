namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Represents a PennMUSH attribute as read from a database file.
/// </summary>
public class PennMUSHAttribute
{
	/// <summary>
	/// Attribute name (may include ` for branches)
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Owner DBRef (if different from object owner)
	/// </summary>
	public int? Owner { get; init; }

	/// <summary>
	/// Attribute flags (no_command, visual, etc.)
	/// </summary>
	public List<string> Flags { get; init; } = [];

	/// <summary>
	/// Number of times dereferenced
	/// </summary>
	public int DerefCount { get; init; }

	/// <summary>
	/// Attribute value (can be multi-line)
	/// </summary>
	public required string Value { get; init; }
}
