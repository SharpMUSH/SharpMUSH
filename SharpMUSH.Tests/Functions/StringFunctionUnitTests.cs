using MarkupString;
using Serilog;
using SharpMUSH.Library.ParserInterfaces;
using System.Text;
using A = MarkupString.MarkupStringModule;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Functions;

public class StringFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

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

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;

		var color = StringExtensions.ansiBytes(expectedBytes);
		var markup = MarkupImplementation.AnsiMarkup.Create(foreground: color);
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
	[Arguments("align(30 30,a,b)",
		"a                              b                             ")]
	[Arguments("align(5 5,a1%ra2,b1)",
		"a1    b1   \na2         ")]
	[Arguments("align(5 5,a1%ra2,b1%rb2%rb3)",
		"a1    b1   \na2    b2   \n      b3   ")]
	[Arguments("align(1. 5 1.,|,this is a test,|)",
		"| this  |\n| is a  |\n| test  |")]
	[Arguments("align(5 >5,a1%ra2,b1%rb2%rb3)",
		"a1       b1\na2       b2\n         b3")]
	[Arguments("align(5. >5,a1,b1%rb2%rb3)",
		"a1       b1\na1       b2\na1       b3")]
	[Arguments("align(5 >5.,a1%ra2%ra3,b1)",
		"a1       b1\na2       b1\na3       b1")]
	[Arguments("align(>30 30,a,b)",
		"                             a b                             ")]
	[Arguments("align(>30 >30,a,b)",
		"                             a                              b")]
	[Arguments("align(3,123 1 1 1 1)",
		"123\n1 1\n1 1")]
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
			? new[] { expectedByte1 }
			: new[] { expectedByte1, expectedByte2.Value };

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;

		var color = StringExtensions.ansiBytes(expectedBytes);
		var markup = MarkupImplementation.AnsiMarkup.Create(background: color);
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
	[Arguments("strlen(hello)", "5")]
	[Arguments("strlen(a b c)", "5")]
	public async Task Strlen(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("left(hello world,5)", "hello")]
	[Arguments("left(abc,10)", "abc")]
	public async Task Left(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("right(hello world,5)", "world")]
	[Arguments("right(abc,10)", "abc")]
	public async Task Right(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("mid(hello world,6,5)", "world")]
	public async Task Mid(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("ucstr(hello)", "HELLO")]
	[Arguments("ucstr(HeLLo WoRLd)", "HELLO WORLD")]
	public async Task Ucstr(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("lcstr(HELLO)", "hello")]
	[Arguments("lcstr(HeLLo WoRLd)", "hello world")]
	public async Task Lcstr(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("repeat(x,5)", "xxxxx")]
	[Arguments("repeat(ab,3)", "ababab")]
	public async Task Repeat(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("space(5)", "     ")]
	[Arguments("space(0)", "")]
	public async Task Space(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("chr(65)", "A")]
	[Arguments("chr(97)", "a")]
	public async Task Chr(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("ord(A)", "65")]
	[Arguments("ord(a)", "97")]
	public async Task Ord(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("flip(hello)", "olleh")]
	[Arguments("flip(abc)", "cba")]
	public async Task Flip(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("edit(this is a test,a test,an exam)", "this is an exam")]
	[Arguments("edit(hello,^,well )", "well hello")]
	[Arguments("edit(hello,$,%bworld)", "hello world")]
	public async Task Edit(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("tr(hello,el,ip)", "hippo")]
	[Arguments("tr(abcd,bd,xy)", "axcy")]
	public async Task Tr(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("merge(a|b|c,1|2|3,|)", "a1 b2 c3")]
	public async Task Merge(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("comp(abc,abc)", "0")]
	[Arguments("comp(abc,def)", "-1")]
	[Arguments("comp(def,abc)", "1")]
	public async Task Comp(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("decompose(ansi(hr,red))", @"ansi\(hr\,red\)")]
	[Arguments("decompose(ansi(ub,red))", @"ansi\(ub\,red\)")]
	public async Task Decompose(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	// TODO: Fix decomposeweb, and then fix this test.
	[Arguments("decomposeweb(ansi(hr,red))", @"<span style=""color:Red;background-color:inherit;text-decoration:inherit"">red</span>")]
	// [Arguments("decomposeweb(ansi(bu,blue))", @"<span style=""color:Blue;background-color:inherit;text-decoration:underline"">blue</span>")]
	// TODO: decompsoe is not matching 'b' correctly it seems.
	// [Skip("Decompose function not functioning as expected. Needs investigation.")]
	public async Task DecomposeWeb(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("cond(1,yes,no)", "yes")]
	[Arguments("cond(0,yes,no)", "no")]
	public async Task Cond(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("strinsert(hello,3,X)", "helXlo")]
	public async Task Strinsert(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("strreplace(hello world,6,5,universe)", "hello universe")]
	public async Task Strreplace(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("strmatch(test,t*)", "1")]
	[Arguments("strmatch(test,x*)", "0")]
	public async Task Strmatch(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("accent(e,')", "é")]
	public async Task Accent(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("brackets(\\[test\\])", "1 1 0 0 0 0")]
	public async Task Brackets(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("lpos(test,t)", "0 3")]
	[Arguments("lpos(test,s)", "2")]
	public async Task Lpos(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("strcat(a,b,c)", "abc")]
	[Arguments("strcat(hello,%b,world)", "hello world")]
	public async Task Strcat(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stripansi(ansi(r,red))", "red")]
	public async Task Stripansi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Comprehensive tests for align() function
	[Test]
	[Arguments("align(10 10,left,right)", "left       right     ")]
	[Arguments("align(>10 >10,left,right)", "      left      right")]
	[Arguments("align(-10 -10,left,right)", "   left      right   ")]
	public async Task AlignJustification(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("align(5X 5X,hello world,test foo)", "hello test ")]
	[Arguments("align(10x 10x,hello%rworld,test%rfoo)", "hello      test      \nworld      foo       ")]
	public async Task AlignTruncate(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("align(10#,test)", "test      ")]
	[Arguments("align(10# 10,test,more)", "test      more      ")]
	public async Task AlignNoColSep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("align(10 10,text,data,-,|)", "text------|data------")]
	[Arguments("align(10 10,text,data,.,=,|)", "text......=data......")]
	public async Task AlignCustomSeparators(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("align(5,this is a long text)", "this \nis a \nlong \ntext ")]
	[Arguments("align(10,word wrap test here)", "word wrap \ntest here ")]
	public async Task AlignWrapping(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Comprehensive tests for lalign() function
	[Test]
	[Arguments("lalign(10 10,col1|col2,|)", "col1       col2      ")]
	[Arguments("lalign(>10 >10,left|right,|)", "      left      right")]
	public async Task LAlign(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lalign(10 10,first|second,|,%b)", "first      second    ")]
	[Arguments("lalign(10 10,a|b,|,-,|)", "a---------|b---------")]
	[Arguments("lalign(5 5,x|y,|,.,|)", "x....|y....")]
	public async Task LAlignCustomDelimitersAndSeparators(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lalign(10 10,a b,%b)", "a          b         ")]
	[Arguments("lalign(5 5 5 5,one|two|three|four,|)", "one   two   three four ")]
	public async Task LAlignMultipleColumns(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lalign(10$,single,|)", "single")]
	[Arguments("lalign(5. 5,a|b,|)", "a     b    ")]
	public async Task LAlignOptions(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}