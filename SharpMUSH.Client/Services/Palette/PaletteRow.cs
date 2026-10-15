using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>What choosing a palette row does.</summary>
public enum PaletteRowKind
{
	/// <summary>Goes to a page of the portal.</summary>
	Go,

	/// <summary>A row the current page added, which does what the page says.</summary>
	Page,

	/// <summary>Runs a command line, once its slots are filled.</summary>
	Run,

	/// <summary>Puts a command line into the Play composer.</summary>
	Insert,

	/// <summary>A line for the <c>@input</c> session capturing the player's lines; needs the terminal.</summary>
	Input,

	/// <summary>A built-in command, put into the composer to finish.</summary>
	Builtin,

	/// <summary>Opens a help topic.</summary>
	Help
}

/// <summary>One row of the command palette.</summary>
/// <param name="Id">What a pin names: an entry's <c>&lt;objid&gt;/&lt;NAME&gt;</c>, <c>go:&lt;route&gt;</c>, <c>cmd:&lt;name&gt;</c>.</param>
/// <param name="Key">What makes two rows the same: the normalised line, or the route.</param>
/// <param name="Info">A second line saying what it does, or why it is offered now.</param>
/// <param name="Hint">A dim word at the end of the row: where it comes from.</param>
/// <param name="Line">The command line, for a row that runs or inserts one.</param>
/// <param name="Order">Where the row's source sits, nearest first, for ties.</param>
/// <param name="Weight">How strongly the context stack raises it, 0 when it does not.</param>
/// <param name="Run">For a page row: what choosing it does.</param>
public sealed record PaletteRow(
	string Id,
	string Key,
	PaletteRowKind Kind,
	string Label,
	string Icon,
	string? Info = null,
	string? Hint = null,
	string? Href = null,
	string? Line = null,
	int Order = 0,
	double Weight = 0,
	bool Urgent = false,
	PaletteEntryView? Entry = null,
	string? Keywords = null,
	Func<Task>? Run = null)
{
	/// <summary>The order of a client row: after every server entry.</summary>
	public const int ClientOrder = 10;

	/// <summary>The order of a built-in: last of all.</summary>
	public const int BuiltinOrder = 20;

	/// <summary>A row that goes to <paramref name="href"/>.</summary>
	public static PaletteRow Go(string href, string icon, string label, string? hint = null)
		=> new($"go:{href}", $"go:{href}", PaletteRowKind.Go, label, icon, Hint: hint, Href: href, Order: ClientOrder);

	/// <summary>A row of the current page's own, which runs <paramref name="run"/>.</summary>
	public static PaletteRow Page(string id, string icon, string label, Func<Task> run, string? info = null)
		=> new($"page:{id}", $"page:{id}", PaletteRowKind.Page, label, icon, Info: info, Order: ClientOrder, Run: run);

	/// <summary>True when choosing it needs slots filled first.</summary>
	public bool HasSlots => Line is not null && Kind is PaletteRowKind.Run or PaletteRowKind.Input
		&& PaletteLines.Slots(Line).Count > 0;
}

/// <summary>A heading and the rows under it, in the empty palette.</summary>
/// <param name="Hidden">Rows past the group's limit, offered by a "more" row.</param>
public sealed record PaletteGroup(string Name, IReadOnlyList<PaletteRow> Rows, int Hidden);
