using ANSILibrary;
using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Markup.Data;

internal static class Substring
{
	public static IEnumerable<(MString str, int length, MString expected)> SubstringLengthData() =>
	[
		(A.single("redCat"), 3, A.single("red")),
		(A.single("redCat"), 0, A.single(string.Empty)),
		(A.single("redCat"), 6, A.single("redCat")),
		(
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"),
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat")),
			3, A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red")
		),
		(
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"),
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat")),
			4,
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"),
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "c"))
		),
		(
			A.multiple([
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Green)), A.single("green")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat"))
			]),
			7, A.multiple([
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Green)), A.single("g"))
			])
		),
		(
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"),
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat")),
			0, A.single(string.Empty)
		)
	];

	public static IEnumerable<(MString str, int start, MString expected)> SubstringData() =>
	[
		(A.single("redCat"), 3, A.single("Cat")),
		(A.single("redCat"), 0, A.single("redCat")),
		(A.single("redCat"), 6, A.single(string.Empty)),
		(A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"), A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat")),
			3, A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat")),
		(A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"), A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat")),
			2,
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "d"),
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat"))),
		(A.multiple([
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Green)), A.single("green")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat"))
			]),
			8, A.multiple([
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Green)), A.single("een")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat"))
			])),
		(A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"), A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat")),
			6, A.single(string.Empty))
	];
}