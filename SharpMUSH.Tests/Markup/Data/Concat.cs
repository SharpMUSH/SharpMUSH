using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.Ansi.AnsiMarkup;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Tests.Markup.Data;

public record ConcatTestData(MString strA, MString strB, MString expected);

internal static class Concat
{
	public static IEnumerable<Func<ConcatTestData>> ConcatData() =>
	[
		() => new(A.single(" "), A.single("woof"), A.single(" woof")),
		() => new(A.single(string.Empty), A.single("woof"), A.single("woof")),
		() => new(A.empty(), A.single("woof"), A.single("woof")),
		() => new(A.single("con"), A.single("cat"), A.single("concat")),
		() => new(A.MarkupSingle2(M.Create(foreground: Color.Red.ToAnsiColor()), A.single("red")), A.single("cat"),
			A.multiple([
				A.MarkupSingle2(M.Create(foreground: Color.Red.ToAnsiColor()), A.single("red")),
				A.single("cat")
			])),
		() => new(A.MarkupSingle2(M.Create(foreground: Color.Red.ToAnsiColor()), A.single("red")),
			A.MarkupSingle(M.Create(foreground: Color.Blue.ToAnsiColor()), "cat"),
			A.multiple([
				A.MarkupSingle2(M.Create(foreground: Color.Red.ToAnsiColor()), A.single("red")),
				A.MarkupSingle2(M.Create(foreground: Color.Blue.ToAnsiColor()), A.single("cat"))
			])),
		() => new(A.MarkupSingle2(M.Create(foreground: Color.Red.ToAnsiColor()), A.single("red")),
			A.concat(A.MarkupSingle(M.Create(foreground: Color.Blue.ToAnsiColor()), "cat"),
				A.MarkupSingle(M.Create(foreground: Color.Red.ToAnsiColor()), "reallyred")),
			A.multiple([
				A.MarkupSingle2(M.Create(foreground: Color.Red.ToAnsiColor()), A.single("red")),
				A.MarkupSingle2(M.Create(foreground: Color.Blue.ToAnsiColor()), A.single("cat")),
				A.MarkupSingle2(M.Create(foreground: Color.Red.ToAnsiColor()), A.single("reallyred"))
			])),
		() => new(A.MarkupSingle2(M.Create(clear: true), A.single("clear")),
			A.MarkupSingle(M.Create(foreground: Color.Blue.ToAnsiColor()), "cat"),
			A.multiple([
				A.MarkupSingle2(M.Create(clear: true), A.single("clear")),
				A.MarkupSingle2(M.Create(foreground: Color.Blue.ToAnsiColor()), A.single("cat"))
			]))
	];
}