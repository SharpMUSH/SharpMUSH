using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Markup.Data;

public record SubstringTestData(MString str, int length, MString expected);

public record SubstringTestData2(MString str, int start, MString expected);

internal static class Substring
{
	public static IEnumerable<Func<SubstringTestData>> SubstringLengthData() =>
	[
		()=> new(A.single("redCat"), 3, A.single("red")),
		()=> new(A.single("redCat"), 0, A.single(string.Empty)),
		()=> new(A.single("redCat"), 6, A.single("redCat")),
		()=> new(
			A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red"),
				A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat")),
			3, A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red")
		),
		()=> new(
			A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red"),
				A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat")),
			4,
			A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red"),
				A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "c"))
		),
		()=> new(
			A.multiple([
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Red)), A.single("red")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), A.single("cat")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Green)), A.single("green")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), A.single("cat"))
			]),
			7, A.multiple([
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Red)), A.single("red")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), A.single("cat")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Green)), A.single("g"))
			])
		),
		()=> new(
			A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red"),
				A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat")),
			0, A.single(string.Empty)
		)
	];

	public static IEnumerable<Func<SubstringTestData2>> SubstringData() =>
	[
		()=> new(A.single("redCat"), 3, A.single("Cat")),
		()=> new(A.single("redCat"), 0, A.single("redCat")),
		()=> new(A.single("redCat"), 6, A.single(string.Empty)),
		()=> new(A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red"), A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat")),
			3, A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat")),
		()=> new(A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red"), A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat")),
			2,
			A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "d"),
				A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat"))),
		()=> new(A.multiple([
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Red)), A.single("red")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), A.single("cat")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Green)), A.single("green")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), A.single("cat"))
			]),
			8, A.multiple([
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Green)), A.single("een")),
				A.MarkupSingle2(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), A.single("cat"))
			])),
		()=> new(A.concat(A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red"), A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Blue)), "cat")),
			6, A.single(string.Empty))
	];
}