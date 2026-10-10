using System.Drawing;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Library.Extensions;

public static class AnsiExtensions
{
	private static readonly AnsiMarkup Bold = AnsiMarkup.Create(bold: true);

	/// <summary>
	/// The markup library's colour for a <see cref="Color"/>. The engine still describes palettes in
	/// <see cref="System.Drawing"/> terms; the markup library deliberately does not depend on it.
	/// </summary>
	public static AnsiColor.Rgb ToAnsiColor(this Color color) => new(color.R, color.G, color.B);

	/// <summary>
	/// Bold, in the reader's theme text colour (<c>tone(foreground,…)</c>), so a name stands out on a light
	/// theme as well as a dark one. A reader with no theme gets bold alone.
	/// </summary>
	public static MString Hilight(this MString str) =>
		MarkupText.Wrap(Bold, ToneMarkup.Build(ThemeRole.Foreground, str, null));

	/// <inheritdoc cref="Hilight(MString)"/>
	public static MString Hilight(this string str) =>
		Hilight(MarkupText.Plain(str));
}