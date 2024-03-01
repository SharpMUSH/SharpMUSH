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

		public static IEnumerable<object[]> SubstringData
		{
			get
			{
				return new object[][] {
					[new AnsiString("abcdef"), 3, new AnsiString("def")],
					[new AnsiString("abcdef"), 0, new AnsiString("abcdef")],
					[new AnsiString("abcdef"), 6, new AnsiString("")],
					[new AnsiString(new(Foreground: "#FF0000"),"red").Concat(new AnsiString(new(Foreground: "#0000FF"),"cat")), 
					 3, new AnsiString(new AnsiString(new(Foreground: "#0000FF"),"cat"))],
					[new AnsiString(new(Foreground: "#FF0000"),"red").Concat(new AnsiString(new(Foreground: "#0000FF"),"cat")), 
					 2, new AnsiString(new(Foreground: "#FF0000"),"d").Concat(new AnsiString(new(Foreground: "#0000FF"),"cat"))],
					[new AnsiString(new(Foreground: "#FF0000"),"red").Concat(new AnsiString(new(Foreground: "#0000FF"),"cat")),
					 6, new AnsiString("")]
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

		[TestMethod]
		[DynamicData(nameof(SubstringData))]
		public void Substring(AnsiString str, int start, AnsiString expected)
		{
			var result = str.Substring(start);

			Log.Logger.Information("{Result}{NewLine}{Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}
	}
}
