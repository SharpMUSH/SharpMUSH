using ANSILibrary;
using NSubstitute;
using Serilog;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Text;
using SharpMUSH.Library.Services.Interfaces;
using A = MarkupString.MarkupStringModule;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Functions;

public class StringFunctionUnitTests 
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("ansi(r,red)", "red", (byte)31, null)]
	[Arguments("ansi(hr,red)", "red", (byte)1, (byte)31)]
	[Arguments("ansi(y,yellow)", "yellow", (byte)33, null)]
	[Arguments("ansi(hy,yellow)", "yellow", (byte)1, (byte)33)]
	public async Task ANSI(string str, string expectedText, byte expectedByte1, byte? expectedByte2)
	{
		Console.WriteLine("Testing: {0}", str);
		var expectedBytes = expectedByte2 is null
			? new[] { expectedByte1 }
			: new[] { expectedByte1, expectedByte2.Value };

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message!;

		var color = StringExtensions.ansiBytes(expectedBytes);
		var markup = MarkupString.MarkupImplementation.AnsiMarkup.Create(foreground: color);
		var markedUpString = A.markupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine,
			markedUpString);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var nextExpectedBytes = Encoding.Unicode.GetBytes(markedUpString.ToString());

		foreach (var bt in resultBytes.Zip(nextExpectedBytes))
		{
			await Assert
				.That(bt.First)
				.IsEqualTo(bt.Second);
		}
	}
	
	[Test]
	[Arguments("digest(md5,rawr)", "56742fd94d4e8f8b22d592186c12a9c5")]
	public async Task Digest(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);		
	}
	
	
	[Test]
	[Arguments("align(30 30,a,b)", "a                              b                             ")]
	[Arguments("align(>30 30,a,b)", "                             a b                             ")]
	[Arguments("align(>30 >30,a,b)", "                             a                              b")]
	public async Task Align(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);		
	}

	[Test]
	[Arguments("ansi(R,red)", "red", (byte)41, null)]
	[Arguments("ansi(hR,red)", "red", (byte)1, (byte)41)]
	[Arguments("ansi(Y,yellow)", "yellow", (byte)43, null)]
	[Arguments("ansi(hY,yellow)", "yellow", (byte)1, (byte)43)]
	public async Task ANSIBackground(string str, string expectedText, byte expectedByte1, byte? expectedByte2)
	{
		Console.WriteLine("Testing: {0}", str);

		var expectedBytes = expectedByte2 is null
			? new byte[] { expectedByte1 }
			: new byte[] { expectedByte1, expectedByte2.Value };

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message!;

		var color = StringExtensions.ansiBytes(expectedBytes);
		var markup = MarkupString.MarkupImplementation.AnsiMarkup.Create(background: color);
		var markedUpString = A.markupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine,
			markedUpString);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var nextExpectedBytes = Encoding.Unicode.GetBytes(markedUpString.ToString());

		foreach (var bt in resultBytes.Zip(nextExpectedBytes))
		{
			await Assert
				.That(bt.First)
				.IsEqualTo(bt.Second);
		}
	}
}