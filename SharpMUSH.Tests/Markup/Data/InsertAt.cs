using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.Ansi.AnsiMarkup;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Tests.Markup.Data;

public record InsertAtTestData(MString str, int index, MString insert, MString expected);

internal static class InsertAt
{
	public static IEnumerable<Func<InsertAtTestData>> InsertAtData() =>
	[
		() => new(A.single("RedCat"), 3, A.single("Kitty"), A.single("RedKittyCat")),
		() => new(A.single("RedCat"), 0, A.single("Kitty"), A.single("KittyRedCat")),
		() => new(A.single("RedCat"), 6, A.single("Kitty"), A.single("RedCatKitty")),
		() => new(A.MarkupSingle(M.Create(foreground: Color.Red.ToAnsiColor()), "red"), 2,
			A.single("a"), A.MarkupSingle(M.Create(foreground: Color.Red.ToAnsiColor()), "read"))
		// Functions, but does not Optimize properly yet.
		// TODO: Investigate why Optimize does not handle this case correctly. Is the code maybe not hitting Optimize?
	];
}