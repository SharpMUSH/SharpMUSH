using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class RegexFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// PennMUSH's regmatch runs an unanchored pcre2_match at offset 0 and returns
	// `subpatterns >= 0` (src/funlist.c:2897, and quick_regexp_match at src/wild.c:610 for the
	// two-argument form). `re_match_flags` is 0 (src/wild.c:53), so nothing anchors the search:
	// a successful substring match returns 1. Anchor the pattern yourself to require the whole
	// string. The sharpfunc.md help text inherited from TinyMUSH says "the entirety of <string>",
	// which is what Penn's own helpfile says and is not what Penn's code does.
	[Test]
	[Arguments("regmatch(test,test)", "1")]
	[Arguments("regmatch(test,t.*t)", "1")]
	[Arguments("regmatch(test123,t.*t)", "1")]
	[Arguments("regmatch(test,tes)", "1")]
	[Arguments("regmatch(test,est)", "1")]
	[Arguments("regmatch(test,es)", "1")]
	[Arguments("regmatch(test,TEST)", "0")]
	[Arguments("regmatch(test,xyz)", "0")]
	[Arguments("regmatch(test,.*)", "1")]
	// An explicit anchor still restricts the match to the whole string.
	[Arguments("regmatch(test,^test$)", "1")]
	[Arguments("regmatch(test,^tes$)", "0")]
	public async Task Regmatch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regmatchi(test,TEST)", "1")]
	[Arguments("regmatchi(TeSt,test)", "1")]
	[Arguments("regmatchi(test,tes)", "1")]
	[Arguments("regmatchi(TEST,es)", "1")]
	[Arguments("regmatchi(test,xyz)", "0")]
	[Arguments("regmatchi(test,^TES$)", "0")]
	public async Task Regmatchi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// PennMUSH sets every requested destination to "" before filling it — src/funlist.c:2906,
	// "Initialize every q-register used to ''" — and leaves it empty when the match failed
	// (src/funlist.c:2947, `if (subpatterns < 0) lbuff[0] = '\0';`). A failed regmatch must
	// therefore clear the destinations it was asked for rather than leave stale values behind.
	// Capture groups reach the pattern as %( and %): SharpMUSH's argument parser ends the call at
	// the first unescaped ')', so a bare (a)(b) would arrive truncated to "(a".
	[Test]
	[Arguments("[setq(rmx,stale)][regmatch(a,z,0:rmx)]%q<rmx>", "0")]
	[Arguments("[setq(rma,A)][setq(rmb,B)][regmatch(zzz,%(q%)%(r%),0:rma 1:rmb)]%q<rma>|%q<rmb>", "0|")]
	[Arguments("[setq(rmp,P)][setq(rmq,Q)][regmatch(zzz,%(q%)%(r%),rmp rmq)]%q<rmp>|%q<rmq>", "0|")]
	// A group that did not participate in a successful match clears its destination too.
	[Arguments("[setq(rmn,N)][regmatch(abc,%(a%)|%(z%),2:rmn)]%q<rmn>", "1")]
	// A successful match still populates its destinations — help sharpfunc, regmatch2.
	[Arguments("[regmatch(cookies=30,%(.+%)=%(.+%),0:rm0 1:rm3 2:rm5)]%q<rm0>|%q<rm3>|%q<rm5>",
		"1cookies=30|cookies|30")]
	// Positional form: the Nth register takes subpattern N.
	[Arguments("[regmatch(abc,%(a%)%(b%),rs0 rs1 rs2)]%q<rs0>|%q<rs1>|%q<rs2>", "1ab|a|b")]
	// A destination named by a subpattern name rather than a number.
	[Arguments("[regmatch(cookies=30,%(?<food>.+%)=%(?<amt>.+%),food:rf amt:ra)]%q<rf>|%q<ra>",
		"1cookies|30")]
	// A capture index the pattern does not have, and a negative one, each clear their destination
	// rather than throwing: PennMUSH's ansi_pcre_copy_substring yields nothing for an out-of-range
	// subpattern, and parse_integer accepts "-1" as a strict integer on the way there.
	[Arguments("[setq(rmo,old)][regmatch(abc,%(a%),99:rmo)]%q<rmo>", "1")]
	[Arguments("[setq(rmv,old)][regmatch(abc,%(a%),-1:rmv)]%q<rmv>", "1")]
	// A subpattern name the pattern does not define does the same.
	[Arguments("[setq(rmu,old)][regmatch(abc,%(?<here>a%),nowhere:rmu)]%q<rmu>", "1")]
	// The two-argument form names no destinations and must touch none: PennMUSH returns from the
	// nargs == 2 branch before any register code runs (src/funlist.c:2871).
	[Arguments("[setq(rmk,keep)][regmatch(a,z)]%q<rmk>", "0keep")]
	[Arguments("[setq(rmj,keep)][regmatch(a,a)]%q<rmj>", "1keep")]
	public async Task RegmatchRegisters(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// PennMUSH reports a destination it cannot use as a register with e_badregname, appended after
	// the boolean (src/funlist.c:2942); "#-1 REGISTER NAME INVALID" is the same text SharpMUSH
	// already returns from setq(). A bare "-" is the one spelling that is rejected silently instead,
	// as an explicit discard (pi_regs_valid_key, src/parse.c:1407).
	[Test]
	[Arguments("[regmatch(abc,%(a%),0:bad$name)]", "1#-1 REGISTER NAME INVALID")]
	// Split at the first colon only: "0:x:y" names the register "x:y", which is unusable. It must
	// not fall through to the positional reading, which would clobber the register named "0".
	[Arguments("[setq(0,orig)][regmatch(abc,%(a%),0:x:y)]|%q0", "1#-1 REGISTER NAME INVALID|orig")]
	// One report per unusable destination.
	[Arguments("[regmatch(abc,%(a%),0:bad$one 1:bad$two)]",
		"1#-1 REGISTER NAME INVALID#-1 REGISTER NAME INVALID")]
	// A bare "-" discards that capture: no register written, no error, later pairs still filled.
	[Arguments("[regmatch(abc,%(a%)%(b%),- 1:rd1)]%q<rd1>", "1a")]
	public async Task RegmatchRejectsUnusableRegisterNames(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// regrab tests (from pennfunc.md)
	[Test]
	[Arguments("regrab(This is testing a test,test)", "testing")]
	[Arguments("regrab(This is testing a test,s$)", "This")] // First word ending in 's'
	[Arguments("regrab(one two three,t.*)", "two")]
	public async Task Regrab(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regrabi(This is testing a test,TEST)", "testing")]
	public async Task Regrabi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// regraball tests (from pennfunc.md)
	[Test]
	[Arguments("regraball(This is testing a test,test)", "testing test")]
	[Arguments("regraball(This is testing a test,s$)", "This is")] // All words ending in 's'
	public async Task Regraball(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regraballi(This is testing a TEST,test)", "testing TEST")]
	public async Task Regraballi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// reglmatch tests (from pennfunc.md)
	[Test]
	[Arguments("reglmatch(I am testing a test,test)", "3")]
	[Arguments("reglmatch(I am testing a test,test$)", "5")]
	[Arguments("reglmatch(I am testing a test,notfound)", "0")]
	public async Task Reglmatch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reglmatchi(I am testing a TEST,test$)", "5")]
	public async Task Reglmatchi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reglmatchall(I am testing a test,test,%b,|)", "3|5")]
	public async Task Reglmatchall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regmatchalli(I am testing a TEST,test,%b,|)", "3|5")]
	public async Task Regmatchalli(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// regedit tests (from pennfunc.md)
	[Test]
	[Arguments("regedit(test,t,T)", "Test")] // First match only
	[Arguments("regedit(test,e,a)", "tast")] // Simple replacement
	public async Task Regedit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regediti(test,T,X)", "Xest")] // Case insensitive
	public async Task Regediti(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regeditall(test,t,T)", "TesT")] // All matches
																							// Note: The capstr function would need to be implemented for this test to work fully
	public async Task Regeditall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regeditalli(TesT,t,X)", "XesX")] // Case insensitive, all matches
	public async Task Regeditalli(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitch(test,t.*,match)", "match")]
	[Arguments("reswitch(test,x.*,nomatch,t.*,match)", "match")]
	[Arguments("reswitch(test,x.*,nomatch,default)", "default")]
	// Penn reswitch.1-4
	[Arguments("reswitch(test STRING,t,1,0)", "1")]
	[Arguments("reswitch(test STRING,t,1,e,2,0)", "1")]
	[Arguments("reswitch(test STRING,E,1,0)", "0")]
	// Penn reswitch.4 — complex regex with special chars
	// NOTE: Skipped — SharpMUSH evaluates NoParse pattern args via ParsedMessage(),
	// so {4}, [A-Z], {6} get consumed by the parser. PennMUSH passes them raw.
	public async Task Reswitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	// Penn reswitchi.9-11
	[Arguments("reswitchi(TEST,t.*,match)", "match")]
	[Arguments("reswitchi(test STRING,t,1,0)", "1")]
	[Arguments("reswitchi(test STRING,t,1,e,2,0)", "1")]
	[Arguments("reswitchi(test STRING,E,1,0)", "1")]
	// Penn reswitchi.12 — complex regex (same NoParse issue as reswitch.4)
	public async Task Reswitchi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchall(test,t.*,match1,e.*,match2)", "match1match2")]
	// Penn reswitchall.5-7
	[Arguments("reswitchall(test STRING,t,1,0)", "1")]
	[Arguments("reswitchall(test STRING,t,1,e,2,0)", "12")]
	[Arguments("reswitchall(test STRING,E,1,0)", "0")]
	// Penn reswitchall.8 — complex regex (same NoParse issue as reswitch.4)
	public async Task Reswitchall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchalli(TEST,t.*,match1,e.*,match2)", "match1match2")]
	// Penn reswitchalli.13-15
	[Arguments("reswitchalli(test STRING,t,1,0)", "1")]
	[Arguments("reswitchalli(test STRING,t,1,e,2,0)", "12")]
	[Arguments("reswitchalli(test STRING,E,1,0)", "1")]
	// Penn reswitchalli.16 — complex regex (same NoParse issue as reswitch.4)
	public async Task Reswitchalli(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires attribute service integration")]
	[Arguments("regrep(#0,*,pattern)", "")]
	public async Task Regrep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires attribute service integration")]
	[Arguments("regrepi(#0,*,pattern)", "")]
	public async Task Regrepi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
