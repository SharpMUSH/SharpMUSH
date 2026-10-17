namespace SharpMUSH.Library.Models.Portal;

/// <summary>What choosing a palette row does with its line.</summary>
public enum PaletteAction
{
	/// <summary>Runs the line, once its slots are filled.</summary>
	Run,

	/// <summary>Puts the line into the Play composer for the player to finish.</summary>
	Insert
}

/// <summary>
/// Where an entry's object sits for the player, nearest first: the order typing the line would reach it,
/// which decides which of two entries with the same line is kept.
/// </summary>
public enum PaletteScope
{
	/// <summary>An object the player's <c>@input</c> session routes lines to.</summary>
	Input,

	/// <summary>The player.</summary>
	Self,

	/// <summary>Something the player carries.</summary>
	Inventory,

	/// <summary>The player's location or something in it.</summary>
	Room,

	/// <summary>The contents of a zone master room.</summary>
	Zone,

	/// <summary>The master room or its contents.</summary>
	Master
}

/// <summary>A palette entry the player may use, as <c>GET api/palette</c> answers it.</summary>
/// <param name="Id"><c>&lt;objid&gt;/&lt;NAME&gt;</c>: the object and the attribute's last segment.</param>
/// <param name="Line">The line as declared, <c>&lt;slots&gt;</c> and all.</param>
/// <param name="System">The context name its object's commands push: the owning package, else the objid.</param>
/// <param name="ObjectName">The name of the object it is on, for a hint when two labels collide.</param>
/// <param name="When">Context names it needs, all of them, to rise.</param>
public sealed record PaletteEntryView(
	string Id,
	string Label,
	string Line,
	PaletteAction Action,
	PaletteScope Scope,
	string System,
	string ObjectName,
	string? Context = null,
	string? Group = null,
	string? Info = null,
	string? Icon = null,
	string? Keywords = null,
	string? Help = null,
	int Order = 0,
	bool Always = false,
	IReadOnlyList<string>? When = null,
	bool Urgent = false);

/// <summary>A context on the player's stack, as the palette reads it.</summary>
/// <param name="Weight">1 when just pushed, halving every 15 minutes.</param>
public sealed record PaletteContextView(string Name, IReadOnlyDictionary<string, string> Values, double Weight, string? Info = null);

/// <summary>The <c>@input</c> session capturing the player's lines.</summary>
/// <param name="Exit">The line that ends it, when the exit pattern is a plain word.</param>
public sealed record PaletteInputView(string Session, string? Exit);

/// <summary>Everything the palette needs from the server, answered once per open.</summary>
/// <param name="Entries">Entries in the order typing their lines would reach them, input entries first.</param>
/// <param name="Builtins">The built-in commands the player may run, by name.</param>
/// <param name="Pins">The ids in the character's <c>PALETTE_PINS</c>, in order.</param>
/// <param name="Stack">The context stack, top first.</param>
/// <param name="Dropped">Entries left out because an object or the open passed its limit.</param>
/// <param name="Invalid">Palette attributes that are not a valid entry, as <c>&lt;objid&gt;/&lt;NAME&gt;: why</c>.</param>
public sealed record PaletteResponse(
	IReadOnlyList<PaletteEntryView> Entries,
	PaletteInputView? Input,
	IReadOnlyList<string> Builtins,
	IReadOnlyList<string> Pins,
	IReadOnlyList<PaletteContextView> Stack,
	int Dropped,
	IReadOnlyList<string> Invalid);

/// <summary>The pins the portal writes back (<c>PUT api/palette/pins</c>), in order.</summary>
public sealed record PalettePinsRequest(IReadOnlyList<string> Pins)
{
	public const int MaxPinLength = 200;

	public static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxPinLength
		&& !id.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
}
