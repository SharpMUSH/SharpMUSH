using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Markup.Data;

internal static class ConcatAttach
{
	public static IEnumerable<Func<ConcatTestData>> ConcatAttachData() =>
	[
		() => new(A.single(" "), A.single("woof"), A.single(" woof")),
		() => new(A.single(string.Empty), A.single("woof"), A.single("woof")),
		() => new(A.empty(), A.single("woof"), A.single("woof")),
		() => new(A.single("con"), A.single("cat"), A.single("concat")),
		() => new(A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")), A.single("cat"),
			A.markupMultiple(M.Create(foreground: StringExtensions.rgb(Color.Red)), [A.single("red"), A.single("cat")])),
		() => new(A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")),
			A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat"),
			A.markupMultiple(M.Create(foreground: StringExtensions.rgb(Color.Red)),
			[
				A.single("red"),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat"))
			])),
		() => new(A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")),
			A.concat(A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat"),
				A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "reallyred")),
			A.markupMultiple(M.Create(foreground: StringExtensions.rgb(Color.Red)), [
				A.single("red"),
				A.concat(
					A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat"),
					A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "reallyred"))
			])),
		() => new(A.markupSingle2(M.Create(clear: true), A.single("clear")),
			A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)), "cat"),
			A.multiple([
				A.markupSingle2(M.Create(clear: true), A.single("clear")),
				A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat"))
			]))
	];
}