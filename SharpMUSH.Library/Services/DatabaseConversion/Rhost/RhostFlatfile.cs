namespace SharpMUSH.Library.Services.DatabaseConversion.Rhost;

/// <summary>
/// A RhostMUSH flatfile as <c>@dump/flat</c> writes it, field by field, before any of it is given a
/// SharpMUSH meaning. <see cref="RhostDatabaseTranslator"/> does that.
/// </summary>
/// <param name="Version">The <c>+V</c> header's value: the format version in the low byte, the rest flags.</param>
/// <param name="DatabaseTop">The <c>+S</c> header: one past the highest object number.</param>
/// <param name="NextAttribute">The <c>+N</c> header: the number the next user attribute would get.</param>
/// <param name="UserAttributes">The <c>+A</c> headers: each user attribute's number, name and flags.</param>
/// <param name="Objects">Every object, in file order.</param>
public sealed record RhostFlatfile(
	int Version,
	int DatabaseTop,
	int NextAttribute,
	IReadOnlyDictionary<int, RhostAttributeDefinition> UserAttributes,
	IReadOnlyList<RhostFlatfileObject> Objects)
{
	/// <summary>The format version, without the flag bits.</summary>
	public int FormatVersion => Version & RhostFlatfileReader.VersionMask;

	/// <summary>Whether a header flag bit is set.</summary>
	public bool Has(int flag) => (Version & flag) != 0;
}

/// <summary>A user attribute as its <c>+A</c> header names it: <c>+A&lt;number&gt;</c>, then <c>&lt;flags&gt;:&lt;name&gt;</c>.</summary>
public sealed record RhostAttributeDefinition(int Number, string Name, uint Flags);

/// <summary>
/// One attribute as an object carries it: the attribute's number (or name, in a keyed dump), and the
/// value with its <c>\x01owner:flags:</c> prefix already split off.
/// </summary>
/// <param name="Number">The attribute number, or -1 when the dump names it instead.</param>
/// <param name="Name">The name, when the dump names it; otherwise empty.</param>
/// <param name="Owner">Who set it, when the value carried a prefix naming one.</param>
/// <param name="Flags">The <c>AF_</c> bits from the prefix; 0 without one.</param>
/// <param name="Value">The text.</param>
public sealed record RhostFlatfileAttribute(int Number, string Name, int? Owner, uint Flags, string Value);

/// <summary>
/// One object record. The dbref fields are as the file has them: RhostMUSH, like TinyMUSH, overloads
/// <c>location</c>, <c>exits</c> and <c>link</c> by type, which the translator sorts out.
/// </summary>
public sealed record RhostFlatfileObject
{
	public required int DBRef { get; init; }
	public required string Name { get; init; }
	public int Location { get; init; } = -1;

	/// <summary>The zone field <c>V_ZONE</c> dumps carry after the location; -1 without one.</summary>
	public int ZoneField { get; init; } = -1;

	public int Contents { get; init; } = -1;
	public int Exits { get; init; } = -1;
	public int Link { get; init; } = -1;
	public int Next { get; init; } = -1;

	/// <summary>The default lock as the header writes it, with bare object numbers; empty for none.</summary>
	public string Lock { get; init; } = string.Empty;

	public int Owner { get; init; } = -1;
	public int Parent { get; init; } = -1;
	public int Pennies { get; init; }

	/// <summary>The four flag words. The object's type is the low three bits of the first.</summary>
	public required uint[] Flags { get; init; }

	/// <summary>The eight toggle words: toggles, then powers (3-5) and depowers (6-8).</summary>
	public required uint[] Toggles { get; init; }

	/// <summary>The zone list: a ZONEMASTER's members, any other object's zones.</summary>
	public IReadOnlyList<int> Zones { get; init; } = [];

	public IReadOnlyList<RhostFlatfileAttribute> Attributes { get; init; } = [];

	/// <summary>The object type, from the first flag word.</summary>
	public RhostObjectType Type => (RhostObjectType)(Flags[0] & 0x7);
}

/// <summary>RhostMUSH's object types (<c>TYPE_*</c>).</summary>
public enum RhostObjectType
{
	Room = 0,
	Thing = 1,
	Exit = 2,
	Player = 3,
	Zone = 4,
	Garbage = 5
}
