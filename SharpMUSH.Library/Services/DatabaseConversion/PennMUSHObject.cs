namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Represents a PennMUSH object as read from a database file.
/// </summary>
public class PennMUSHObject
{
	/// <summary>
	/// Database reference number
	/// </summary>
	public required int DBRef { get; init; }

	/// <summary>
	/// Object name
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Location DBRef (#-1 for nothing, #-3 for garbage)
	/// </summary>
	public int Location { get; init; }

	/// <summary>
	/// First object in contents list
	/// </summary>
	public int Contents { get; init; }

	/// <summary>
	/// First exit
	/// </summary>
	public int Exits { get; init; }

	/// <summary>
	/// Link/destination DBRef
	/// </summary>
	public int Link { get; init; }

	/// <summary>
	/// Next object in linked list
	/// </summary>
	public int Next { get; init; }

	/// <summary>
	/// Owner DBRef
	/// </summary>
	public int Owner { get; init; }

	/// <summary>
	/// Parent DBRef
	/// </summary>
	public int Parent { get; init; }

	/// <summary>
	/// Zone DBRef (master room)
	/// </summary>
	public int Zone { get; init; }

	/// <summary>
	/// Pennies/money
	/// </summary>
	public int Pennies { get; init; }

	/// <summary>
	/// Object type: ROOM, THING, EXIT, PLAYER
	/// </summary>
	public required PennMUSHObjectType Type { get; init; }

	/// <summary>
	/// Object flags (DARK, WIZARD, etc.)
	/// </summary>
	public List<string> Flags { get; init; } = [];

	/// <summary>
	/// Object powers
	/// </summary>
	public List<string> Powers { get; init; } = [];

	/// <summary>
	/// Warning flags
	/// </summary>
	public List<string> Warnings { get; init; } = [];

	/// <summary>
	/// Creation timestamp (Unix time)
	/// </summary>
	public long CreationTime { get; init; }

	/// <summary>
	/// Last modification timestamp (Unix time)
	/// </summary>
	public long ModificationTime { get; init; }

	/// <summary>
	/// Object attributes
	/// </summary>
	public List<PennMUSHAttribute> Attributes { get; init; } = [];

	/// <summary>
	/// Locks on the object
	/// </summary>
	public Dictionary<string, string> Locks { get; init; } = [];

	/// <summary>
	/// Player password (for PLAYER type only)
	/// </summary>
	public string? Password { get; init; }

	/// <summary>
	/// Player aliases (for PLAYER type only)
	/// </summary>
	public List<string> Aliases { get; init; } = [];
}

/// <summary>
/// PennMUSH object types
/// </summary>
public enum PennMUSHObjectType
{
	Room = 0,
	Thing = 1,
	Exit = 2,
	Player = 3
}
