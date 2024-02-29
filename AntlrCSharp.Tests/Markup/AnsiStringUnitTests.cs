using AntlrCSharp.Implementation.Markup;
using Serilog;
using AnsiString = AntlrCSharp.Implementation.Markup.MarkupSpan<AntlrCSharp.Implementation.Markup.AnsiMarkup>;

namespace AntlrCSharp.Tests.Markup
{
	[TestClass]
	public class AnsiStringUnitTests : BaseUnitTest
	{
		public static IEnumerable<object[]> ConcatData
		{
			get
			{
				return new [] {
					(object[]) [new AnsiString("con"), new AnsiString("cat"), new AnsiString("concat")],
					(object[]) [new AnsiString(new AnsiMarkup(Foreground: "#FF0000"),"red"), new AnsiString("cat"), new AnsiString("redcat")]
				};
			}
		}

	[TestMethod]
	[DynamicData(nameof(ConcatData))]
	public void Concat(AnsiString strA, AnsiString strB, AnsiString expected)
	{
		var result = strA.Concat(strB);

		Log.Logger.Information("{Result} VS {Expected}", result, expected);

		Assert.AreEqual(expected.ToString(), result.ToString());
	}
}
}
