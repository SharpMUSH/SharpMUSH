using ANSILibrary;
using Serilog;
using System.Text;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Functions;

[TestClass]
public class StringFunctionUnitTests: BaseUnitTest
{

	[TestMethod]
	[DataRow("ansi(r,red)", "red", (byte)31)]
	[DataRow("ansi(hr,red)", "red", (byte)1,(byte)31)]
	[DataRow("ansi(y,yellow)", "yellow", (byte)33)]
	[DataRow("ansi(hy,yellow)", "yellow", (byte)1, (byte)33)]
	public void ANSI(string str, string expectedText, params byte[] expectedBytes)
	{
		Console.WriteLine("Testing: {0}", str);

		var parser = TestParser();
		var result = parser.FunctionParse(MModule.single(str))?.Message!;

		var color = StringExtensions.ansiBytes(expectedBytes);
		var markup = MarkupString.MarkupImplementation.AnsiMarkup.Create(foreground: color);
		var markedUpString = A.markupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, markedUpString);
		CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(markedUpString.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
	}

	[TestMethod]
	[DataRow("ansi(R,red)", "red", (byte)41)]
	[DataRow("ansi(hR,red)", "red", (byte)1, (byte)41)]
	[DataRow("ansi(Y,yellow)", "yellow", (byte)43)]
	[DataRow("ansi(hY,yellow)", "yellow", (byte)1, (byte)43)]
	public void ANSIBackground(string str, string expectedText, params byte[] expectedByte)
	{
		Console.WriteLine("Testing: {0}", str);

		var parser = TestParser();
		var result = parser.FunctionParse(MModule.single(str))?.Message!;

		var color = StringExtensions.ansiBytes(expectedByte);
		var markup = MarkupString.MarkupImplementation.AnsiMarkup.Create(background: color);
		var markedUpString = A.markupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, markedUpString);
		CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(markedUpString.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
	}
}
