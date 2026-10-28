using System.Globalization;
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
	/// Permission scopes (<c>control.all</c>, <c>server.operate</c>) the object is granted as allow
	/// overrides: a source privilege SharpMUSH grants as a permission rather than as a power.
	/// </summary>
	public List<string> Permissions { get; init; } = [];

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
	public Dictionary<string, PennMUSHLock> Locks { get; init; } = [];

	/// <summary>
	/// A player's stored password, as the source wrote it: its XYXXY attribute in a PennMUSH dump.
	/// </summary>
	public string? Password { get; init; }

	/// <summary>
	/// An exit's or player's aliases: its ALIAS attribute in a PennMUSH dump, split on ';'.
	/// </summary>
	public List<string> Aliases { get; init; } = [];

	/// <summary>
	/// Powers the source took away from this object, as SharpMUSH power names. PennMUSH has none; a
	/// RhostMUSH depower becomes one, and the import denies the power's permission on the object.
	/// </summary>
	public List<string> DeniedPowers { get; init; } = [];

	/// <summary>
	/// Roles the object holds by slug, beyond those its flags and powers stand for: a RhostMUSH staff level
	/// becomes one, from <see cref="PennMUSHDatabase.Roles"/>.
	/// </summary>
	public List<string> Roles { get; init; } = [];

	/// <summary>
	/// The reality masks the object received and transmitted on in the source (a RhostMUSH or TinyMUX object's levels), which
	/// <see cref="PennMUSHDatabase.Reality"/> names; null when it had none of its own.
	/// </summary>
	public PennMUSHReality? Reality { get; init; }

	/// <summary>
	/// A RhostMUSH object's ten totem words, which hold its totems as bits; empty when it had none. The import makes
	/// a flag of each totem the object holds.
	/// </summary>
	public IReadOnlyList<uint> Totems { get; init; } = [];

	/// <summary>The RhostMUSH tags that name the object: the world's, and those personal to an owner.</summary>
	public IReadOnlyList<PennMUSHTag> Tags { get; init; } = [];
}

/// <summary>A tag that names an object: the world's when <paramref name="Owner"/> is null, else that owner's own.</summary>
public sealed record PennMUSHTag(string Name, int? Owner);

/// <summary>An object's reality masks: the levels it perceives on, and those it is perceived on.</summary>
public sealed record PennMUSHReality(uint Receive, uint Transmit)
{
	/// <summary>
	/// The masks as RhostMUSH and TinyMUX store them: eight hex digits received and eight transmitted, with a space
	/// between, or null for a value of any other shape, which both servers ignore.
	/// </summary>
	public static PennMUSHReality? Parse(string value)
		=> value.Length == 17 && value[8] == ' '
			&& uint.TryParse(value.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var receive)
			&& uint.TryParse(value.AsSpan(9), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var transmit)
				? new PennMUSHReality(receive, transmit)
				: null;
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
