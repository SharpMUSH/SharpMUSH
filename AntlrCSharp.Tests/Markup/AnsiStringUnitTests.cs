using AntlrCSharp.Implementation.Markup;
using Serilog;

namespace AntlrCSharp.Tests.Markup
{
	[TestClass]
	public class AnsiStringUnitTests : BaseUnitTest
	{
		[TestMethod]
		[DataRow("con", "cat", "concat")]
		public void Concat(string strA, string strB, string expected)
		{
			var result = MarkupSpan<AnsiMarkup>.Concat(new MarkupSpan<AnsiMarkup>(strA), new MarkupSpan<AnsiMarkup>(strB));
			var exp = new MarkupSpan<AnsiMarkup>(expected);

			Log.Logger.Information("{Result} VS {Expected}", result, exp);

			Assert.AreEqual(exp.ToString(), result.ToString());
		}
	}
}
