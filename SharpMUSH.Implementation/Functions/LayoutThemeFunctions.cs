using System.Collections.Immutable;
using System.Globalization;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Layout;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// Layout themes: <c>themes()</c> lists the named ones, <c>theme()</c> writes one out as JSON, and
/// <c>swatch()</c> shows one's colours.
/// </summary>
public partial class Functions
{
	/// <summary><c>themes()</c> — the names of the built-in themes, space separated.</summary>
	[SharpFunction(Name = "themes", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> Themes(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult(new CallState(string.Join(' ', LayoutThemes.Names)));

	/// <summary>
	/// <c>theme(&lt;theme&gt;)</c> — the theme a name or a JSON description makes, written out in full as
	/// JSON: its name, mode, and each colour with its standard colour. Generate once and keep the result.
	/// </summary>
	[SharpFunction(Name = "theme", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["theme"])]
	public ValueTask<CallState> Theme(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult(LayoutThemes.Read(Arg(parser.CurrentState.ArgumentsOrdered, 0).ToPlainText()) switch
		{
			ThemePalette palette => new CallState(palette.ToJson()),
			Error<string> error => new CallState(error.Value),
		});

	/// <summary>
	/// <c>swatch(&lt;theme&gt;[, &lt;width&gt;])</c> — a table of the theme's colours: each role, a sample in
	/// its colour, the colour, the standard colour a sixteen-colour client is sent, and its contrast with
	/// the background, marked when it is under what the role needs, or <c>client</c> for a standard colour
	/// alone, which looks however the reader's client makes it.
	/// </summary>
	[SharpFunction(Name = "swatch", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["theme", "width"])]
	public ValueTask<CallState> Swatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(LayoutThemes.Read(Arg(args, 0).ToPlainText()) switch
		{
			ThemePalette palette => Finish(parser, SwatchTable(palette), Arg(args, 1), theme: palette.ToLayoutTheme()),
			Error<string> error => new CallState(error.Value),
		});
	}

	private static Table SwatchTable(ThemePalette palette)
	{
		var background = palette.BackgroundColor;
		var shortfalls = palette.Check();
		var rows = ImmutableArray.CreateBuilder<ImmutableArray<Block>>();
		foreach (var role in Enum.GetValues<ThemeRole>())
		{
			if (palette[role] is not { } color) continue;
			var shortfall = shortfalls.FirstOrDefault(found => found.Role == role);
			var ratio = role == ThemeRole.Background ? "-"
				// A standard colour looks however the reader's client makes it.
				: color.Rgb is null || palette[ThemeRole.Background] is { Rgb: null } ? "client"
				: ColorMath.Contrast(color.Resolved, background).ToString("0.0", CultureInfo.InvariantCulture) + ":1"
					+ (shortfall.Role == role && shortfall.Required > 0 ? $" (needs {shortfall.Required.ToString("0.#", CultureInfo.InvariantCulture)})" : string.Empty);
			rows.Add(
			[
				MarkupText.Plain(ThemePalette.RoleName(role)),
				role == ThemeRole.Background ? MarkupText.Plain("-") : MarkupText.Wrap(AnsiTheme.Paint(color), "Sample"),
				MarkupText.Plain(color.Rgb?.ToHex() ?? "-"),
				MarkupText.Plain(color.Slot is { } slot ? StandardName(slot) : "-"),
				MarkupText.Plain(ratio),
			]);
		}

		return new Table(
			[
				new TableColumn(MarkupText.Plain("Role")),
				new TableColumn(MarkupText.Plain("Sample")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Colour")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("16-colour")) { Priority = 3 },
				new TableColumn(MarkupText.Plain("Contrast")) { Wrap = false, Priority = 2 },
			],
			rows.ToImmutable());
	}

	/// <summary>A standard colour's number and name: <c>12 bright blue</c>.</summary>
	private static string StandardName(int slot)
	{
		string[] names = ["black", "red", "green", "yellow", "blue", "magenta", "cyan", "white"];
		return $"{slot} {(slot >= 8 ? "bright " : string.Empty)}{names[slot % 8]}";
	}
}
