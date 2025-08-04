using ANSILibrary;
using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Markup.Data;

public record SplitTestData(
	MString str, string delimiter, MString[] expected);

internal static class Split
{
	public static IEnumerable<Func<SplitTestData>> SplitData() =>
	[
		() => new (
			A.concat(A.single("con"), A.single(";cat")), ";",
			new[]
			{
				A.single("con"),
				A.single("cat")
			}
		),
		() => new (
			A.concat(A.single("wide"), A.single(";;delimiter")), ";;",
			new[]
			{
				A.single("wide"),
				A.single("delimiter")
			}
		),
		() => new (
			A.concat(A.single("wide;"), A.single(";delimiter")), ";;",
			new[]
			{
				A.single("wide"),
				A.single("delimiter")
			}
		),
		() => new (
			A.concat(A.concat(A.single("widest;"), A.single(";")), A.single(";delimiter")), ";;;",
			new[]
			{
				A.single("widest"),
				A.single("delimiter")
			}
		),
		() => new (
			A.concat(A.single("ca"), A.single(";t")), "",
			new[]
			{
				A.single(""),
				A.single("c"),
				A.single("a"),
				A.single(";"),
				A.single("t")
			}
		),
		() => new (
			A.concat(A.single(";con"), A.single(";cat;")), ";",
			new[]
			{
				A.single(""),
				A.single("con"),
				A.single("cat"),
				A.single("")
			}
		),
		() => new (
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"), A.single(";cat")), ";",
			new[]
			{
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red"),
				A.single("cat")
			}
		),
		() => new (
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "r;e;d"), A.single("c;at")), ";",
			new[]
			{
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "r"),
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "e"),
				A.multiple([A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "d"), A.single("c")]),
				A.single("at")
			}
		)
	];
}