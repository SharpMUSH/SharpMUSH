using System.Drawing;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

namespace SharpMUSH.Library.Extensions;

public static class AnsiExtensions
{
	/// <summary>
	/// Bold + bright white foreground (SGR codes 1;37), matching the MUSH ansi("hw", …) convention.
	/// </summary>
	private static readonly AnsiMarkup HILIGHT = AnsiCodeParser.Parse("hw");

	/// <summary>
	/// The markup library's colour for a <see cref="Color"/>. The engine still describes palettes in
	/// <see cref="System.Drawing"/> terms; the markup library deliberately does not depend on it.
	/// </summary>
	public static AnsiColor.Rgb ToAnsiColor(this Color color) => new(color.R, color.G, color.B);

	public static MString Hilight(this MString str) =>
		MarkupText.Wrap(HILIGHT, str);

	public static MString Hilight(this string str) =>
		MarkupText.Wrap(HILIGHT, MarkupText.Plain(str));
}