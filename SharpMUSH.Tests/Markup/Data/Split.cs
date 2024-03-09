using A = MarkupString.MarkupStringModule;
using AnsiString = MarkupString.MarkupStringModule.MarkupString;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup.Data
{
	internal static class Split
	{
		public static IEnumerable<object[]> SplitData
		{
			get
			{
				return new object[][]
				{
					[A.concat(A.single("con"), A.single(";cat")), ";",
							new AnsiString[]
							{
									A.single("con"),
									A.single("cat")
							}],
					[A.concat(A.single("ca"), A.single(";t")), "",
							new AnsiString[]
							{
									A.single(""),
									A.single("c"),
									A.single("a"),
									A.single(";"),
									A.single("t")
							}],
					[A.concat(A.single(";con"), A.single(";cat;")), ";",
							new AnsiString[]
							{
									A.single(""),
									A.single("con"),
									A.single("cat"),
									A.single("")
							}],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"), A.single(";cat")), ";",
						new AnsiString[] {
								A.markupSingle(M.Create(foreground: "#FF0000"), "red"),
								A.single("cat")
						}],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"r;e;d"), A.single("c;at")), ";",
						new AnsiString[]
						{
								A.markupSingle(M.Create(foreground: "#FF0000"), "r"),
								A.markupSingle(M.Create(foreground: "#FF0000"), "e"),
								A.multiple([A.markupSingle(M.Create(foreground: "#FF0000"), "d"), A.single("c")]),
								A.single("at")
						}]
				};
			}
		}
	}
}