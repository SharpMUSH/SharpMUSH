using ANSILibrary;
using MarkupString;
using System.Drawing;
using static ANSILibrary.ANSI;

namespace SharpMUSH.Library.Extensions;

public static class AnsiExtensions
{
	private static readonly AnsiColor WHITE = ANSILibrary.StringExtensions.rgb(Color.White);

	public static MString Hilight(this MString str) =>
		MModule.markupSingle2(MarkupImplementation.AnsiMarkup.Create(foreground: WHITE), str);

	public static MString Hilight(this string str) =>
		MModule.markupSingle2(MarkupImplementation.AnsiMarkup.Create(foreground: WHITE), MModule.single(str));
}