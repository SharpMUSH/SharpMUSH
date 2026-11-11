using MarkupString;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's counting, wildcard and register functions (randpos(), nummember(), numwildmatch(), setqmatch(),
/// listmatch(), pushregs(), ofparse()), its bracket checkers (parenstr(), parenmatch()) and bypass().
/// </summary>
public class RhostMatchingFunctionTests : ServerTestBase
{
	private async Task<MString> Markup(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))?.Message ?? MarkupText.Empty;

	private async Task<string> Ansi(string code) => (await Markup(code)).Render(MarkupFormat.Ansi);

	[Test]
	[Arguments("randpos(i,this is weird)", "3 6 11")]
	[Arguments("randpos(aa,aaaa)", "1 2 3")]
	[Arguments("randpos(z,this is weird)", "")]
	[Arguments("randpos(,abc)", "1 2 3")]
	public async Task RandPos(string code, string sorted)
	{
		var positions = (await Eval(code)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Order();
		await Assert.That(string.Join(' ', positions)).IsEqualTo(sorted);
	}

	[Test]
	[Arguments("nummember(This is a test test test repeat a test, test)", "4")]
	[Arguments("nummember(This is another test, fred)", "0")]
	[Arguments("nummember(This is a test, *e*)", "0")]
	[Arguments("nummember(a A a,a)", "2")]
	[Arguments("nummember(a|b|a,a,|)", "2")]
	[Arguments("numwildmatch(This is a test, test)", "1")]
	[Arguments("numwildmatch(This test is a test of nummatch.  A test is fun, test)", "3")]
	[Arguments("numwildmatch(This test *e* is silly *ed, fred)", "2")]
	[Arguments("numwildmatch(TEST b?? t*,test)", "2")]
	[Arguments("numwildmatch(a*|b*|ab,abc,|)", "1")]
	public async Task Counting(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[setqmatch(This is a test, *test)] -- [r(0)]", "1 -- This is a ")]
	[Arguments("[setqmatch(open the door hal, open * ???)] -- [r(0)] -- [r(2)]", "1 -- the door -- a")]
	[Arguments("setqmatch(kow, wok)", "0")]
	[Arguments("setqmatch(kow, wok, n)", "")]
	[Arguments("[setqmatch(kow, k*, n)]-[r(0)]", "-ow")]
	[Arguments("setqmatch(abc,abc)", "0")]
	[Arguments("[setq(0,old)][setqmatch(kow,x*)]-[r(0)]", "0-old")]
	[Arguments("[setq(0,old)][setqmatch(xy,x*y)]-[r(0)]", "0-old")]
	[Arguments("setqmatch(abc,abc*)", "0")]
	[Arguments("[setq(1,old)][setqmatch(axb,a*b*)]-[r(0)]-[r(1)]", "1-x-old")]
	public async Task SetQMatch(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("listmatch(This is a test, *test)", "This is a ")]
	[Arguments("listmatch(hit the troll with the sword, hit * with *, /)", "the troll/the sword")]
	[Arguments("listmatch(hit the troll with the sword, hit * with *, /, 0 1)", "the troll/the sword")]
	[Arguments("listmatch(hit the troll with the sword, hit * with *, /, 1)", "the sword")]
	[Arguments("listmatch(hit the troll with the sword, hit * with *, /, 0)", "the troll")]
	[Arguments("listmatch(hit the troll with the sword, hit * with *, /, 1 0)", "the troll/the sword")]
	[Arguments("listmatch(a-b, *-*,)", "a b")]
	[Arguments("listmatch(kow, wok)", "")]
	[Arguments("listmatch(-b,*-*,/)", "b")]
	[Arguments("listmatch(a-,*-*)", "a")]
	[Arguments("listmatch(a b,* *,,%b)", "")]
	public async Task ListMatch(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[setq(0,one)]-[r(0)]-[setq(0,two)]-[r(0)]-", "-one--two-")]
	[Arguments("[setq(0,one)]-[r(0)]-[pushregs(1)][setq(0,two)]-[pushregs(0)][r(0)]-", "-one--one-")]
	[Arguments("[setq(0,one)][setq(1,two)]-%q0%q1-[pushregs(+0)][setq(0,xxx)][setq(1,abc)]-%q0%q1-[pushregs(-0)]--%q0%q1--",
		"-onetwo--xxxabc---oneabc--")]
	[Arguments("[setq(a,one)][pushregs(+a)][setq(a,two)][pushregs(-A)]%qa", "one")]
	[Arguments("[setq(0,one)][pushregs(1)][setq(0,two)][pushregs(0)][setq(0,three)][pushregs(0)]-%q0-", "--")]
	[Arguments("[setq(0,one)][pushregs(-0)]%q0", "one")]
	[Arguments("pushregs()", "#-1 FUNCTION REQUIRES ONE ARGUMENT")]
	[Arguments("[setq(0,one)][pushregs(%b)]%q0", "one")]
	public async Task PushRegs(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ofparse(1,0,2)", "2")]
	[Arguments("ofparse(1,10,11,0)", "10")]
	[Arguments("ofparse(1,#-1 Error)", "#-1 Error")]
	[Arguments("ofparse(2,#-1,#101,#2970,,#-3,0,#319,null(This Doesn't Count),|)", "#101|#2970|#319")]
	[Arguments("ofparse(2,foo,0,#-1,bar,baz,)", "foobarbaz")]
	[Arguments("ofparse(2,foo,0,#-1,bar,baz,%b)", "foo bar baz")]
	[Arguments("ofparse(2,a,b,|x)", "a|b")]
	[Arguments("ofparse(2,00,0.0,%b%b0,|)", "00|0.0")]
	[Arguments("ofparse(3,Mike,0,Bob)", "0")]
	[Arguments("ofparse(3,Mike,Bob)", "Bob")]
	[Arguments("ofparse(4,a,0,#-2,,b,|)", "0|#-2|")]
	[Arguments("ofparse(5,,x,y)", "x")]
	[Arguments("ofparse(5,,,y)", "y")]
	[Arguments("ofparse(6,,a,,b,-)", "a-b")]
	[Arguments("ofparse(7,a,,b,dflt)", "")]
	[Arguments("ofparse(7,a,b,dflt)", "dflt")]
	[Arguments("ofparse(8,,a,,|)", "|")]
	[Arguments("[ofparse(1,yes,setq(0,ran)x,no)]-%q0-", "yes--")]
	[Arguments("ofparse(9,a,b)", "#-1 UNKNOWN TYPE SPECIFIED.  SELECT BETWEEN 1-8")]
	[Arguments("ofparse(2,a)", "#-1 FUNCTION (OFPARSE KEYTYPE 2) EXPECTS 3 OR MORE ARGUMENTS [RECEIVED 2]")]
	public async Task OfParse(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>The examples in each function's help topic.</summary>
	[Test]
	[Arguments("nummember(a test test repeat a test,test)", "3")]
	[Arguments("numwildmatch(This test *e* is silly *ed,fred)", "2")]
	[Arguments("[setqmatch(open the door hal,open * ???)] [r(0)]", "1 the door")]
	[Arguments("listmatch(hit the troll with the sword,hit * with *,/)", "the troll/the sword")]
	[Arguments("listmatch(hit the troll with the sword,hit * with *,/,1)", "the sword")]
	[Arguments("[setq(0,one)][pushregs(1)][setq(0,two)]%q0 [pushregs(0)]%q0", "two one")]
	[Arguments("[setq(a,x)][pushregs(+a)][setq(a,y)][pushregs(-a)]%qa", "x")]
	[Arguments("ofparse(1,0,#-1,first,second,none)", "first")]
	[Arguments("ofparse(2,foo,0,#-1,bar,%b)", "foo bar")]
	[Arguments("parenstr([add(1,2))])", "[add(1,2))]")]
	[Arguments("[bypass(1)]done", "done")]
	public async Task HelpExamples(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bypass(1)")]
	[Arguments("bypass(0)")]
	[Arguments("bypass()")]
	public async Task Bypass(string code)
	{
		await Assert.That(await Eval(code)).IsEqualTo(string.Empty);
	}

	[Test]
	[Arguments("parenstr([add(1,2))])", "[add(1,2))]")]
	[Arguments("parenstr(![add(1,2))])", "[add(1,2))]")]
	[Arguments("parenstr(a,b)", "a,b")]
	[Arguments("parenstr(a\\(b)", "a\\(b")]
	[Arguments("parenstr(plain)", "plain")]
	public async Task ParenStrKeepsTheText(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task ParenStrColoursOnlyTheMismatchWithBang()
	{
		await Assert.That(await Ansi("parenstr(![add(1,2))])")).IsEqualTo(await Ansi("\\[add\\(1,2\\)[ansi(hr,\\))]\\]"));
		await Assert.That(await Ansi("parenstr(!a\\(b)")).IsEqualTo("a\\(b")
			.Because("an escaped bracket is not counted, so nothing is mismatched or coloured");
	}

	[Test]
	public async Task ParenStrColoursEveryBracketUpToTheMismatch()
	{
		var matched = await Ansi("parenstr(a(b)c)");
		await Assert.That(matched).IsNotEqualTo("a(b)c");
		await Assert.That(matched).Contains("\u001b[");
		await Assert.That(await Ansi("parenstr(plain)")).IsEqualTo("plain");

		var mismatched = await Ansi("parenstr([add(1,2))])");
		await Assert.That(mismatched).Contains("\u001b[31m)");
		await Assert.That(mismatched).EndsWith("\u001b[0m]").Because("the text after the mismatch is left as it is");
	}

	[Test]
	public async Task ParenMatchReadsAnAttribute()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "ParenMatch");
		await Cmd($"&VA {thing}=[add(1,2)]");
		await Cmd($"&VB {thing}=[add(1,2))]");
		await Cmd($"&VC {thing}=x(y[z]");

		await Assert.That(await Eval($"parenmatch({thing}/VA)")).IsEqualTo("[add(1,2)]");
		await Assert.That(await Eval($"parenmatch({thing}/VB,1)")).IsEqualTo("[add(1,2))]");
		await Assert.That(await Ansi($"parenmatch({thing}/VB,1)")).IsEqualTo(await Ansi("\\[add\\(1,2\\)[ansi(hr,\\))]\\]"));
		await Assert.That(await Eval($"parenmatch({thing}/VA,,,bps)")).IsEqualTo("[ add( 1, 2 ) ]");
		await Assert.That(await Eval($"parenmatch({thing}/VB,,,bps)")).IsEqualTo("[ add( 1, 2 ) )]");
		await Assert.That(await Ansi($"parenmatch({thing}/VC,1)")).IsEqualTo(await Ansi("x[ansi(hr,\\()]y\\[z\\]"))
			.Because("with nothing closed wrongly, the last opener left open is the mismatch");
		await Assert.That(await Eval($"parenmatch({thing}/VC,,,p)")).IsEqualTo("x( y[z]")
			.Because("a mismatched opener keeps the space after it, as a matched one would");
		await Assert.That(await Eval($"parenmatch({thing}/NOSUCH)")).IsEqualTo(string.Empty);
	}
}
