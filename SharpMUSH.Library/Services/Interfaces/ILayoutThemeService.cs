using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>A layout theme as <c>@theme/list</c> shows it.</summary>
/// <param name="Name">Its name, lower case.</param>
/// <param name="BuiltIn">Whether it ships with SharpMUSH or MarkupString rather than being added by the game.</param>
/// <param name="Disabled">Whether staff turned it off (built-in themes only).</param>
/// <param name="Definition">An added theme's JSON; null for a built-in one.</param>
public sealed record LayoutThemeEntry(string Name, bool BuiltIn, bool Disabled, string? Definition);

/// <summary>
/// The layout themes this game offers: the built-in presets less the ones staff disabled, and the ones staff
/// added (<c>@theme/add</c>). Every place softcode or a player names a theme reads it here, so a disabled theme
/// cannot be chosen and an added one can.
/// </summary>
public interface ILayoutThemeService
{
	/// <summary>The palette <paramref name="spec"/> names or writes out, or the error saying why not.</summary>
	Result<ThemePalette> Read(string spec);

	/// <summary>
	/// <paramref name="spec"/> with the game's added themes written out in it, so it reads without them: what a
	/// connection is sent. A theme that is disabled or not known is an error; other faults are left for
	/// <see cref="Read"/> to report.
	/// </summary>
	Result<string> Resolve(string spec);

	/// <summary>The names a theme can be chosen by: the enabled built-in ones, then the added ones.</summary>
	IEnumerable<string> Names { get; }

	/// <summary>Every theme, built-in ones first, disabled ones included.</summary>
	IReadOnlyList<LayoutThemeEntry> List();

	/// <summary>Reads the stored themes. Run once at startup, before softcode runs.</summary>
	ValueTask LoadAsync();

	/// <summary>
	/// Adds the theme <paramref name="name"/>, or replaces the one added under it, as <paramref name="definition"/>
	/// (JSON, or another theme's name). An added theme it builds on is written out in it, so it does not change
	/// when that one does.
	/// </summary>
	ValueTask<Result<LayoutThemeEntry>> AddAsync(string name, string definition);

	/// <summary>Removes the added theme <paramref name="name"/>.</summary>
	ValueTask<FoundResult<Success>> RemoveAsync(string name);

	/// <summary>Disables or enables the built-in theme <paramref name="name"/>.</summary>
	ValueTask<FoundResult<Success>> SetDisabledAsync(string name, bool disabled);
}
