using MarkupString;
using MarkupString.MarkupImplementation;

namespace SharpMUSH.Library.Extensions;

public static class AnsiExtensions
{
	/// <summary>
	/// Bold + bright white foreground (SGR codes 1;37), matching the MUSH ansi("hw", …) convention.
	/// </summary>
	private static readonly AnsiMarkup HILIGHT = AnsiCodeParser.ParseCodes("hw");

	public static MString Hilight(this MString str) =>
		MModule.MarkupSingle2(HILIGHT, str);

	public static MString Hilight(this string str) =>
		MModule.MarkupSingle2(HILIGHT, MModule.single(str));
}