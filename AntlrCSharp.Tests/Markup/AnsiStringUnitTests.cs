using ANSIConsole;
using AntlrCSharp.Implementation.Markup;
using Serilog;
using System.Collections.Immutable;
using System.Text;
using AnsiString = AntlrCSharp.Implementation.Markup.MarkupSpan<AntlrCSharp.Implementation.Markup.AnsiMarkup>;

namespace AntlrCSharp.Tests.Markup
{
	[TestClass]
	public class AnsiStringUnitTests : BaseUnitTest
	{
		public AnsiStringUnitTests()
		{
			ANSIInitializer.Init(false);
			ANSIInitializer.Enabled = true;
		}


		public static IEnumerable<object[]> ConcatData
		{
			get
			{
				return new AnsiString[][] {
					[new AnsiString("con"), new AnsiString("cat"), new AnsiString("concat")],
					[new AnsiString(new AnsiMarkup(Foreground: "#FF0000"),"red"), new AnsiString("cat"),
					 new AnsiString(null, (new AnsiString[] {new(new(Foreground: "#FF0000"),"red"), new("cat") }).ToImmutableList())],
					[new AnsiString(new AnsiMarkup(Foreground: "#FF0000"),"red"), new AnsiString(new AnsiMarkup(Foreground: "#0000FF"),"cat"),
					 new AnsiString(null, (new AnsiString[] {new(new(Foreground: "#FF0000"),"red"), new(new(Foreground: "#0000FF"),"cat") }).ToImmutableList())]
				};
			}
		}

		[TestMethod]
		[DynamicData(nameof(ConcatData))]
		public void Concat(AnsiString strA, AnsiString strB, AnsiString expected)
		{
			var result = strA.Concat(strB);

			Log.Logger.Information("{Result}{NewLine}{Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}
	}
}
