namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Represents a PennMUSH object as read from a database file.
/// </summary>
/// <remarks>A dbref field left unset is -1, PennMUSH's NOTHING: #0 is a real room.</remarks>
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
	/// Where the object is: a thing's or player's location, an exit's source. NOTHING for a room.
	/// </summary>
	/// <remarks>
	/// PennMUSH overloads its <c>location</c> and <c>exits</c> fields by type (hdrs/dbdefs.h); the
	/// parser sorts them into this and <see cref="Link"/> by what they mean.
	/// </remarks>
	public int Location { get; init; } = -1;

	/// <summary>
	/// First object in contents list
	/// </summary>
	public int Contents { get; init; } = -1;

	/// <summary>
	/// A room's first exit; NOTHING for anything else.
	/// </summary>
	public int Exits { get; init; } = -1;

	/// <summary>
	/// An exit's destination, a thing's or player's home, a room's drop-to.
	/// </summary>
	public int Link { get; init; } = -1;

	/// <summary>
	/// Next object in linked list
	/// </summary>
	public int Next { get; init; } = -1;

	/// <summary>
	/// Owner DBRef
	/// </summary>
	public int Owner { get; init; } = -1;

	/// <summary>
	/// Parent DBRef
	/// </summary>
	public int Parent { get; init; } = -1;

	/// <summary>
	/// Zone DBRef (the zone master object)
	/// </summary>
	public int Zone { get; init; } = -1;

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
	/// Creation timestamp, in Unix <em>seconds</em> — PennMUSH stores a time_t.
	/// </summary>
	/// <remarks>
	/// SharpMUSH stores milliseconds, so anything carrying this into a SharpObject scales by 1000
	/// (<see cref="PennMUSHDatabaseConverter.PennTimestamps"/>).
	/// </remarks>
	public long CreationTime { get; init; }

	/// <summary>
	/// Last modification timestamp, in Unix <em>seconds</em>. See <see cref="CreationTime"/> on units.
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
	/// A player's stored password, as the source wrote it: its XYXXY attribute in a PennMUSH dump.
	/// </summary>
	public string? Password { get; init; }

	/// <summary>
	/// An exit's or player's aliases: its ALIAS attribute in a PennMUSH dump, split on ';'.
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
