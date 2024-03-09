using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup.Data
{
	internal static class Substring
	{
		public static IEnumerable<object[]> SubstringLengthData
		{
			get
			{
				return new object[][] {
					[A.single("redCat"), 3, A.single("red")],
					[A.single("redCat"), 0, A.single(string.Empty)],
					[A.single("redCat"), 6, A.single("redCat")],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 3, A.markupSingle(M.Create(foreground: "#FF0000" ), "red")],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 4, A.concat(A.markupSingle(M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "c"))],
					[A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat")),
											 A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("green")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))]),
					 7, A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
													A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat")),
													A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("g"))])],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 0, A.single(string.Empty)]
				};
			}
		}

		public static IEnumerable<object[]> SubstringData
		{
			get
			{
				return new object[][] {
					[A.single("redCat"), 3, A.single("Cat")],
					[A.single("redCat"), 0, A.single("redCat")],
					[A.single("redCat"), 6, A.single(string.Empty)],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 3, A.markupSingle(M.Create(foreground: "#0000FF" ), "cat")],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 2, A.concat(A.markupSingle(M.Create(foreground: "#FF0000"),"d"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat"))],
					[A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat")),
											 A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("green")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))]),
					 8, A.multiple([A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("een")),
													A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))])],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 6, A.single(string.Empty)]
				};
			}
		}
	}
}