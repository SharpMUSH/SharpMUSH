using ANSILibrary;
using Serilog;
using System.Text;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Functions;

public class StringFunctionUnitTests: BaseUnitTest
{
	[Test]
	[Arguments("ansi(r,red)", "red", (byte)31, null)]
	[Arguments("ansi(hr,red)", "red", (byte)1,(byte)31)]
	[Arguments("ansi(y,yellow)", "yellow", (byte)33, null)]
	[Arguments("ansi(hy,yellow)", "yellow", (byte)1, (byte)33)]
	public async Task ANSI(string str, string expectedText, byte expectedByte1, byte? expectedByte2)
	{
		Console.WriteLine("Testing: {0}", str);
		var expectedBytes = expectedByte2 == null 
			? new byte[] { expectedByte1 }
			: new byte[] { expectedByte1, expectedByte2.Value };
		
		
		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message!;

		var color = StringExtensions.ansiBytes(expectedBytes);
		var markup = MarkupString.MarkupImplementation.AnsiMarkup.Create(foreground: color);
		var markedUpString = A.markupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, markedUpString);
		
		await Assert
			.That(Encoding.Unicode.GetBytes(result.ToString()))
			.IsEqualTo(Encoding.Unicode.GetBytes(markedUpString.ToString()));
	}

	[Test]
	[Arguments("ansi(R,red)", "red", (byte)41, null)]
	[Arguments("ansi(hR,red)", "red", (byte)1, (byte)41)]
	[Arguments("ansi(Y,yellow)", "yellow", (byte)43, null)]
	[Arguments("ansi(hY,yellow)", "yellow", (byte)1, (byte)43)]
	public async Task ANSIBackground(string str, string expectedText, byte expectedByte1, byte? expectedByte2)
	{
		Console.WriteLine("Testing: {0}", str);

		var expectedBytes = expectedByte2 == null 
			? new byte[] { expectedByte1 }
			: new byte[] { expectedByte1, expectedByte2.Value };
		
		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message!;

		var color = StringExtensions.ansiBytes(expectedBytes);
		var markup = MarkupString.MarkupImplementation.AnsiMarkup.Create(background: color);
		var markedUpString = A.markupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, markedUpString);
		
		await Assert
			.That(Encoding.Unicode.GetBytes(result.ToString()))
			.IsEqualTo(Encoding.Unicode.GetBytes(markedUpString.ToString()));
	}
}
