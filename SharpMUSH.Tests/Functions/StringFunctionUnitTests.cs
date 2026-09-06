using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using Serilog;
using SharpMUSH.Library.ParserInterfaces;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Functions;

public class StringFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// The colour is a palette index plus a brightness flag now, not a byte stream, and the
	// comparison is on the rendered ANSI rather than on ToString() — which is plain text.
	[Test]
	[Arguments("ansi(r,red)", "red", (byte)1, false)]
	[Arguments("ansi(hr,red)", "red", (byte)1, true)]
	[Arguments("ansi(y,yellow)", "yellow", (byte)3, false)]
	[Arguments("ansi(hy,yellow)", "yellow", (byte)3, true)]
	public async Task ANSI(string str, string expectedText, byte paletteIndex, bool bright)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;

		var markup = AnsiMarkup.Create(foreground: new AnsiColor.Standard(paletteIndex, bright));
		var markedUpString = A.MarkupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine,
			markedUpString);

		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo(markedUpString.Render(MarkupFormat.Ansi));
	}

	[Test]
	[Arguments("digest(md5,rawr)", "6f10ae4af2b1216275234f1b4f4040ef")]
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

	// A background has no bright variant, so `h` in front of one is the bold attribute — see
	// AnsiCodeParser. Compared on the rendered ANSI, since ToString() is now plain text.
	[Test]
	[Arguments("ansi(R,red)", "red", (byte)1, false)]
	[Arguments("ansi(hR,red)", "red", (byte)1, true)]
	[Arguments("ansi(Y,yellow)", "yellow", (byte)3, false)]
	[Arguments("ansi(hY,yellow)", "yellow", (byte)3, true)]
	public async Task ANSIBackground(string str, string expectedText, byte paletteIndex, bool bold)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;

		var markup = AnsiMarkup.Create(background: new AnsiColor.Standard(paletteIndex, false), bold: bold);
		var markedUpString = A.MarkupSingle2(markup, A.single(expectedText));

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine,
			markedUpString);

		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo(markedUpString.Render(MarkupFormat.Ansi));
	}

	[Test]
	// after() returns everything after the delimiter, NOT including it (PennMUSH semantics).
	[Arguments("after(abcXYdef,XY)", "def")]
	[Arguments("after(/profile/God,profile/)", "God")]
	[Arguments("after(hello world,lo)", " world")]
	[Arguments("after(abc,XY)", "")] // delimiter absent -> empty
	[Arguments("after(abcdef,abc)", "def")] // delimiter at the start
	public async Task After(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	// before() returns everything before the delimiter, NOT including it; whole string if absent.
	[Arguments("before(abcXYdef,XY)", "abc")]
	[Arguments("before(/profile/God,/God)", "/profile")]
	[Arguments("before(abc,XY)", "abc")] // delimiter absent -> whole string
	public async Task Before(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
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
	[Arguments("edit(hello,^,well )", "wellhello")]
	[Arguments("edit(hello,$,%bworld)", "hello world")]
	public async Task Edit(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("tr(hello,el,ip)", "hippo")]
	[Arguments("tr(abcd,bd,xy)", "axcy")]
	// Penn tr.1-tr.7
	[Arguments("tr(test STRING,,)", "test STRING")]
	[Arguments("tr(test STRING,t,)", "#-1 STRING LENGTHS MUST BE EQUAL")]
	[Arguments("tr(test STRING,,t)", "#-1 STRING LENGTHS MUST BE EQUAL")]
	[Arguments("tr(test STRING,t,f)", "fesf STRING")]
	[Arguments("tr(test STRING,tT,fF)", "fesf SFRING")]
	[Arguments("tr(test STRING,Tt,Ff)", "fesf SFRING")]
	[Arguments("tr(test STRING,te,et)", "etse STRING")]
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
	[Arguments("comp(#1,#2,D)", "-1")]
	[Arguments("comp(#2,#1,D)", "1")]
	[Arguments("comp(#1,#1,D)", "0")]
	[Arguments("comp(#1:12345,#2:12345,D)", "-1")]
	[Arguments("comp(#2:12345,#1:12345,D)", "1")]
	[Arguments("comp(#1:12345,#1:99999,D)", "0")]
	public async Task Comp(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("decompose(ansi(hr,red))", @"ansi\(hr\,red\)")]
	[Arguments("decompose(ansi(ub,red))", @"ansi\(ub\,red\)")]
	// Penn decompose.3: tab and newline characters → %t and %r
	[Arguments("decompose(tab\treturn\n)", "tab%treturn%r")]
	// AnsiColor.Default round-trips through its letter code rather than being dropped silently.
	[Arguments("decompose(ansi(d,x))", @"ansi\(d\,x\)")]
	[Arguments("decompose(ansi(D,x))", @"ansi\(D\,x\)")]
	public async Task Decompose(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	/// <summary>
	/// An xterm-256 palette colour reconstructs as <c>+xtermN</c> / <c>/+xtermN</c> rather than
	/// being dropped, for <see cref="AnsiColor.Xterm"/> values that were never resolved to a
	/// concrete RGB triple (the way the <c>ansi()</c> function's own <c>+xtermN</c> syntax resolves
	/// immediately via the colour config). This markup is what a legacy PennMUSH database's
	/// <c>38;5;N</c> escape codes decode to, so the input here is built directly rather than
	/// through <c>ansi()</c>, which cannot express it.
	/// </summary>
	[Test]
	public async Task Decompose_XtermForeground_ReconstructsAsPlusXtermCode()
	{
		var coloredX = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(200)), "x");
		var source = MarkupText.Concat(MarkupText.Plain("decompose("), MarkupText.Concat(coloredX, MarkupText.Plain(")")));

		var result = (await Parser.FunctionParse(source))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo(@"ansi\(+xterm200\,x\)");
	}

	[Test]
	public async Task Decompose_XtermBackground_ReconstructsAsSlashPlusXtermCode()
	{
		var coloredX = MarkupText.Wrap(AnsiMarkup.Create(background: new AnsiColor.Xterm(200)), "x");
		var source = MarkupText.Concat(MarkupText.Plain("decompose("), MarkupText.Concat(coloredX, MarkupText.Plain(")")));

		var result = (await Parser.FunctionParse(source))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo(@"ansi\(/+xterm200\,x\)");
	}

	[Test]
	// TODO: Fix decomposeweb, and then fix this test.
	// hr is bright red, which resolves through the xterm palette to #FF5555 rather than to a
	// System.Drawing named colour.
	[Arguments("decomposeweb(ansi(hr,red))", @"<span style=""color:#FF5555;background-color:inherit;text-decoration:inherit"">red</span>")]
	// TODO: decompsoe is not matching 'b' correctly it seems.
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
	// Penn strinsert.1-strinsert.3
	[Arguments("strinsert(000,1,1)", "0100")]
	[Arguments("strinsert(000,5,11)", "00011")]
	// Penn strinsert.2 — negative index
	[Arguments("strinsert(000,-1,1)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task Strinsert(string str, string expectedText)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expectedText);
	}

	[Test]
	[Arguments("strreplace(hello world,6,5,universe)", "hello universe")]
	// Penn strreplace.1-strreplace.6
	[Arguments("strreplace(0010,2,1,0)", "0000")]
	[Arguments("strreplace(0010,2,5,011)", "00011")]
	[Arguments("strreplace(0010,2,2,0)", "000")]
	[Arguments("strreplace(0010,2,1,010)", "000100")]
	[Arguments("strreplace(0010,6,1,0)", "0010")]
	// Penn strreplace.6 — negative index
	[Arguments("strreplace(0010,-1,4,woot)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
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

	// Penn trimpenn.1-trimpenn.4
	[Test]
	[Arguments("trimpenn(XXXfooXXX,X,l)", "fooXXX")]
	[Arguments("trimpenn(XXXfooXXX,X,r)", "XXXfoo")]
	[Arguments("trimpenn(XXXfooXXX,X,b)", "foo")]
	[Arguments("trimpenn(XXXfooXXX,Y,l)", "XXXfooXXX")]
	public async Task Trimpenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn trimtiny.1-trimtiny.4
	[Test]
	[Arguments("trimtiny(XXXfooXXX,L,X)", "fooXXX")]
	[Arguments("trimtiny(XXXfooXXX,R,X)", "XXXfoo")]
	[Arguments("trimtiny(XXXfooXXX,B,X)", "foo")]
	[Arguments("trimtiny(XXXfooXXX,l,Y)", "XXXfooXXX")]
	public async Task Trimtiny(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn trim.2-trim.3 (Penn arg order, tiny_trim_fun=no default)
	[Test]
	[Arguments("trim(XXXfooXXX,X,l)", "fooXXX")]
	[Arguments("trim(%b%bfoo%b%b)", "foo")]
	public async Task Trim(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}