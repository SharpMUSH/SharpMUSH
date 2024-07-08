using ANSILibrary;
using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup.Data
{
	internal static class Concat
	{

		public static IEnumerable<object[]> ConcatData
		{
			get	
			{
				return
				[
					[A.single("con"), A.single("cat"), A.single("concat")],
					[A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")), A.single("cat"),
						A.multiple([A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")),
																		A.single("cat")])],
					[A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")), A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Blue)),"cat"),
						A.multiple([A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Red)), A.single("red")),
																		A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.Blue)), A.single("cat"))])]
				];
			}
		}
	}
}