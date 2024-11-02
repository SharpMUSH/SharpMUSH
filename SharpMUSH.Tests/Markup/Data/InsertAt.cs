using ANSILibrary;
using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup.Data;

internal static class InsertAt
{
	public static IEnumerable<(MString str, int index, MString insert, MString expected)> InsertAtData() =>
	[
		(A.single("RedCat"), 3, A.single("Kitty"), A.single("RedKittyCat")),
		(A.single("RedCat"), 0, A.single("Kitty"), A.single("KittyRedCat")),
		(A.single("RedCat"), 6, A.single("Kitty"), A.single("RedCatKitty")),
		(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"), 2,
			A.single("a"), A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "read"))
	];
}