using ANSILibrary;
using Serilog;
using System.Text;
using A = MarkupString.MarkupStringModule;


namespace SharpMUSH.Tests.Functions
{
	[TestClass]
	public class StringFunctionUnitTests: BaseUnitTest
	{

		[TestMethod]
		[DataRow("ansi(r,red)", "red", (byte)31)]
		[DataRow("ansi(hr,red)", "red", (byte)91)]
		public void ANSI(string str, string expectedText, byte expectedByte)
		{
			Console.WriteLine("Testing: {0}", str);

			var parser = TestParser();
			var result = parser.FunctionParse(str)?.Message!;

			var red = StringExtensions.ansiByte(expectedByte);
			var markup = MarkupString.MarkupImplementation.AnsiMarkup.Create(foreground: red);
			var markedUpString = A.markupSingle2(markup, A.single(expectedText));

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, markedUpString);
			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(markedUpString.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}
	}
}
