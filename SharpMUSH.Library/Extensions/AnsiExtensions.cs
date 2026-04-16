using ANSILibrary;
using MarkupString;
using MarkupString.MarkupImplementation;
using System.Drawing;

namespace SharpMUSH.Library.Extensions;

public static class AnsiExtensions
{
	private static readonly AnsiColor WHITE = new AnsiColor.RGB(Color.White);

	public static MString Hilight(this MString str) =>
		MModule.MarkupSingle2(AnsiMarkup.Create(foreground: WHITE), str);

	public static MString Hilight(this string str) =>
		MModule.MarkupSingle2(AnsiMarkup.Create(foreground: WHITE), MModule.single(str));
}