using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace AntlrCSharp.Tests.Markup.Data
{
	internal static class Concat
	{

		public static IEnumerable<object[]> ConcatData
		{
			get
			{
				return new object[][]
				{
					[A.single("con"), A.single("cat"), A.single("concat")],
					[A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")), A.single("cat"),
						A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
																		A.single("cat")])],
					[A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")), A.markupSingle(M.Create(foreground: "#0000FF"),"cat"),
						A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
																		A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))])]
				};
			}
		}
	}
}