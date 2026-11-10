using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The functions TinyMUX games call that PennMUSH has no name for, and the TinyMUX argument forms of
/// functions both have. Expected answers are TinyMUX 2.14's documented ones where it gives them.
/// </summary>
public class MuxFunctionTests : ServerTestBase
{
	private static IDisposable QueueFunctions(bool on)
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxQueueFunctions = on }
		});

	[Test]
	[Arguments("strip(This is a test,is)", "Th  a tet")]
	[Arguments("strip(abcdefgABCDEFG,azGy!7$B)", "bcdefgACDEF")]
	[Arguments("strip(ansi(r,red))", "red")]
	[Arguments("strip(This is a test,is,1)", "isiss")]
	[Arguments("[between(1,10,5)][between(1,10,10)][between(1,10,10,1)][between(1.5,2.5,2)]", "1011")]
	[Arguments("between(a,10,5)", ErrorMessages.Returns.Numbers)]
	[Arguments("[roman(1999)] [roman(4)] [roman(3999)]", "MCMXCIX IV MMMCMXCIX")]
	[Arguments("roman(0)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("[roman(4000)] [roman(777777)] [roman(3999999)]", "Mv dcclxxvMMDCCLXXVII mmmcmxcMxCMXCIX")]
	[Arguments("roman(4000000)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("roman(x)", ErrorMessages.Returns.Integer)]
	[Arguments("[crc32(MUX)] [crc32(M,UX)] [crc32()]", "629126998 629126998 0")]
	[Arguments("[strmem(This is a test)] [strmem(chr(233))]", "14 2")]
	[Arguments("ledit(apple banana lime plum,banana plum,yellow purple)", "apple yellow lime purple")]
	[Arguments("ledit(apple|banana|lime|plum,banana|lime,yellow,|,+)", "apple+yellow++plum")]
	[Arguments("ledit(a b c,d,x)", "a b c")]
	[Arguments("ledit(a b A,a,x)", "x b A")]
	[Arguments("moon(947172800)", "The Moon is New")]
	[Arguments("moon(948382400)", "The Moon is Full")]
	[Arguments("moon(999999999)", "The Moon is Waning Gibbous (72% of Full)")]
	[Arguments("moon(949000000)", "The Moon is Waning Gibbous (50% of Full)")]
	[Arguments("moon(947811600)", "The Moon is Waxing Half (50% of Full)")]
	[Arguments("moon(947797200)", "The Moon is Waxing Crescent (48% of Full)")]
	[Arguments("[moon(948000000,1)] [moon(999999999,1)]", "74 -72")]
	[Arguments("moon(1000000000000)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("moon(-100000000000)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("moon(x)", ErrorMessages.Returns.Number)]
	[Arguments("lrand(5,5,3,|)", "5|5|5")]
	[Arguments("[lrand(1,6,0)][lrand(6,1,3)]", "")]
	[Arguments("[distribute(10,1)] [distribute(0,3,|)]", "10 0|0|0")]
	[Arguments("distribute(1,0)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("distribute(1000001,1)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("distribute(x,1)", ErrorMessages.Returns.Integers)]
	[Arguments("[choose(red blue green,0 1 0)] [choose(red|blue|green,5 -1 0,|)]", "blue red")]
	[Arguments("choose(red blue,1)", ErrorMessages.Returns.ListsMustBeEqualSize)]
	[Arguments("choose(red blue,0 0)", "")]
	[Arguments("bittype(#1)", "7")]
	public async Task TinyMuxFunctions(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	[Test]
	[Arguments("ansi(r,Red,g,Green)", "RedGreen")]
	[Arguments("ansi(r,Red,g)", "#-1 FUNCTION (ANSI) EXPECTS AN EVEN NUMBER OF ARGUMENTS BUT GOT 3")]
	[Arguments("extract(a|b|c|d,2,2,|,-)", "b-c")]
	[Arguments("extract(a|b|c|d,2,2,|,)", "bc")]
	[Arguments("insert(a|c,2,b,|,-)", "a-b-c")]
	[Arguments("remove(a|b|c,b,|,-)", "a-c")]
	[Arguments("extract(a|b|c|d,2,2,|,@@)", "bc")]
	[Arguments("remove(a|b|c,b,|,@@)", "ac")]
	[Arguments("insert(a|b,3,x,|,-)", "a-b-x")]
	[Arguments("insert(a|b,3,x,|)", "a|b")]
	[Arguments("secs(utc,8)", ErrorMessages.Returns.InvalidPrecision)]
	public async Task TinyMuxArgumentForms(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	[Test]
	[Arguments("[andbool(1,abc,#5,-3)] [andbool(1,0)] [andbool(1,#-1 NO MATCH)] [andbool()]", "1 0 0 1")]
	[Arguments("[orbool()] [orbool(0,,#-1,0.000,NaN,-Inf,Ind,0e5)] [orbool(0,x)] [orbool(0,#0)]", "0 0 1 1")]
	[Arguments("[andbool(null(x))] [andbool(1,null(x))] [candbool()] [candbool(null(x))] [corbool()]", "0 0 1 0 0")]
	[Arguments("[candbool(0,[setq(0,ran)])][r(0)]", "0")]
	[Arguments("[candbool(1,[setq(0,ran)]1)][r(0)]", "1ran")]
	[Arguments("[corbool(1,[setq(0,ran)])][r(0)]", "1")]
	[Arguments("[corbool(0,[setq(0,ran)])][r(0)]", "0ran")]
	[Arguments("[israt(.5)][israt(5.)][israt( -5 )][israt(.)][israt(+)][israt(abc)][israt(NaN)][israt(1.2.3)]", "11100000")]
	[Arguments("[butlast()]|[lrest(a)]|[butlast(a  b   c)]", "||a b")]
	[Arguments("[delextract(a b c,0,1)] [delextract(a b c,2,0)] [delextract(a b c,5,1)]", "b c a b c a b c")]
	[Arguments("caplist(it is what it is)", "It is What it Is")]
	[Arguments("caplist(war AND peace)", "War and Peace")]
	[Arguments("caplist(alpha|beta,|,-)", "Alpha-Beta")]
	[Arguments("caplist(1st place)", "1st Place")]
	[Arguments("lcat(a|b|c,|,)", "abc")]
	[Arguments("[lcat(a b c,,@@)]|[caplist(a b,,@@)]|[lcat(a b,,-)]", "abc|AB|a-b")]
	[Arguments("[lband()] [lbor()] [lbxor()] [lband(,|)]", "-1 0 0 -1")]
	[Arguments("lbor(1 x)", ErrorMessages.Returns.Integers)]
	[Arguments("[limath(mod,-7 3)] [limath(mod,7 -3)] [limath(div,-7 2)] [limath(sub,10 3 2)]", "2 -2 -3 5")]
	[Arguments("[limath(min,3 -1 2)] [limath(max,3 -1 2)] [limath(median,3 1 2)] [limath(median,-3 -2)] [limath(add,)]", "-1 3 2 -3 0")]
	[Arguments("limath(sum,9223372036854775807 1)", "-9223372036854775808")]
	[Arguments("limath(ADD,1|2,|)", "3")]
	[Arguments("limath(div,1 0)", ErrorMessages.Returns.DivisionByZero)]
	[Arguments("limath(foo,1)", ErrorMessages.Returns.UnknownOperation)]
	[Arguments("[sha1()] [sha1(ansi(r,abc))]", "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709 A9993E364706816ABA3E25717850C26C9CD0D89D")]
	[Arguments("translate(a%rb%tc,0)", "a b c")]
	[Arguments("translate(ansi(r,red),s)", "red")]
	[Arguments("wrapcolumns(one two three four five six,5,2)", "one  four \ntwo  five \nthreesix  ")]
	[Arguments("wrapcolumns(one two three four five six,5,2,R,<,|,>,1)", "<  one|  two>\n<three| four>\n< five|  six>")]
	[Arguments("wrapcolumns(abcdefgh,3,1,C)", "abc\ndef\ngh ")]
	[Arguments("wrapcolumns(ab cd%ref,2,3)", "abcdef")]
	[Arguments("strlen(wrapcolumns(repeat(ab%b,100000),2,1))", "299999")]
	[Arguments("[wrapcolumns(,5000,2)]|", "|")]
	[Arguments("wrapcolumns(a,1,2000000,L,,repeat(x,1000))", ErrorMessages.Returns.OutputTooLarge)]
	[Arguments("wrapcolumns(repeat(a,8000),1,1,L,repeat(x,8000))", ErrorMessages.Returns.OutputTooLarge)]
	[Arguments("[iter(a b,inum())] [iter(a b,iter(x y,inum()))]|[inum()]|", "1 2 1 2 1 2||")]
	public async Task TinyMuxShims(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	[Test]
	[Arguments("caplist([sound(chime.wav)]alpha beta)", "Alpha Beta")]
	[Arguments("caplist(sound(chime.wav))", "")]
	[Arguments("wrapcolumns([sound(chime.wav)]abc,3,1)", "abc")]
	[Arguments("wrapcolumns(ab [sound(chime.wav)]cd,2,1)", "ab\ncd")]
	[Arguments("wrapcolumns(sound(chime.wav),3,1)", "   ")]
	[Arguments("wrapcolumns(ab%b[sound(chime.wav)]%bcd,2,1)", "ab\ncd")]
	[Arguments("wrapcolumns(ab [sound(chime.wav)],2,1)", "ab")]
	[Arguments("subeval(a[sound(chime.wav)]\\%#b)", "a#1b")]
	[Arguments("subeval(%b[sound(chime.wav)]%b)", "  ")]
	[Arguments("subeval(\\[[sound(chime.wav)]\\])", "[]")]
	[Arguments("subeval(\\%\\%[sound(chime.wav)])", "%")]
	[Arguments("subeval(sound(chime.wav))", "")]
	[Arguments("subeval(\\%[beep()]x)", "x")]
	[Arguments("subeval(\\%q[beep()]x)", "x")]
	[Arguments("subeval(\\%=<a[beep()]>x)", "x")]
	public async Task MuxShimsKeepPoints(string expression, string expected)
	{
		var result = (await Parser.FunctionParse(MString.Plain(expression)))!.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
		await Assert.That(result.Text.Length - result.ToPlainText().Length).IsEqualTo(1);
	}

	[Test]
	[Arguments("url_unescape(hello\\%20world)", "hello world")]
	[Arguments("url_unescape(hello+world)", "hello world")]
	[Arguments("url_unescape(\\%41\\%42\\%43)", "ABC")]
	[Arguments("[url_unescape(a\\%2Bb)] [url_unescape(\\%C3\\%A9)]", "a+b é")]
	[Arguments("[playmem()] [playmem(me)]", "0 0")]
	public async Task TinyMuxVariants(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	/// <summary>pfind() answers pmatch()'s answer for a player, and finds a room by dbref where pmatch() does not.</summary>
	[Test]
	public async Task PfindMatchesAnyDbref()
	{
		await Assert.That(await Eval("pfind(#1)")).IsEqualTo(await Eval("pmatch(#1)"));
		await Assert.That(await Eval("pfind(#0)")).StartsWith("#0:");
		await Assert.That(await Eval("pmatch(#0)")).StartsWith("#-1");
	}

	/// <summary>subeval() reads <c>%=&lt;n&gt;</c> as TinyMUX does: up to three digits, the rest of the name ignored.</summary>
	[Test]
	public async Task SubevalReadsArgumentNumbersAsTinyMux()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MuxSubevalArgs")})");
		await Eval($"attrib_set({thing}/TXT,\\%=<1a>|\\%=<01>|\\%=<001>|\\%=<0001>|\\%=<0>)");
		await Eval($"attrib_set({thing}/FN,lit(subeval(v(TXT))))");
		await Assert.That(await Eval($"u({thing}/FN,zero,one)")).IsEqualTo("one|one|one||zero");
	}

	/// <summary>subeval() reads <c>%=&lt;name&gt;</c> for any name TinyMUX allows, and gives nothing for one it does not.</summary>
	[Test]
	public async Task SubevalReadsAttributeNamesAsTinyMux()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MuxSubevalAttr")})");
		await Eval($"attrib_set({thing}/A?B,qmark)");
		await Eval($"attrib_set({thing}/A#B,hash)");
		await Eval($"attrib_set({thing}/TXT,\\%=<A?B>|\\%=<A#B>|\\%=<A\\(B>|\\%=<A=B>|\\%=<A<B>|\\%=<1A%%B>)");
		await Eval($"attrib_set({thing}/FN,lit(subeval(v(TXT))))");
		await Assert.That(await Eval($"u({thing}/FN)")).IsEqualTo("qmark|hash||||");
	}

	/// <summary>pfind(me) is the caller whatever it is, as TinyMUX's lookup_player has it.</summary>
	[Test]
	public async Task PfindMeFindsAThing()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MuxPfindMe")})");
		await Eval($"attrib_set({thing}/FN,lit([pfind(me)] [pfind(*me)] [pfind(**me)]))");
		var objid = await Eval($"objid({thing})");
		await Assert.That(await Eval($"u({thing}/FN)")).IsEqualTo($"{objid} {objid} {ErrorMessages.Returns.NoMatch}");
	}

	/// <summary>pfind() answers a miss with the error alone, as TinyMUX's quiet matches do; pmatch() tells the caller.</summary>
	[Test]
	public async Task PfindIsQuietOnAMiss()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("MuxPfind");
		var mediator = WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
		var caller = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, mediator, token);
		var room = (await Cmd($"@dig {token}_Room")).Trim().Split(' ')[^1].Trim();
		await Cmd($"@tel #{caller.Number}={room}");
		var parser = WebAppFactoryArg.FunctionParserFor(caller);
		async Task<string> AsCaller(string code) => (await parser.EvaluateAsync(MString.Plain(code))).ToPlainText();

		var before = Notifications.CountFor(caller);
		await Assert.That(await AsCaller("pfind(#99999999)")).IsEqualTo("#-1");
		await Assert.That(await AsCaller("pfind(me)")).IsEqualTo(await AsCaller("objid(me)"));
		await Assert.That(await AsCaller("pfind(*me)")).IsEqualTo(await AsCaller("objid(me)"));
		await Assert.That(await AsCaller("pfind(*%bme)")).IsEqualTo(await AsCaller("objid(me)"));
		await Assert.That(await AsCaller("pfind(**me)")).IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(await AsCaller($"pfind(*#+{caller.Number})")).IsEqualTo(await AsCaller("objid(me)"));
		await Assert.That(await AsCaller($"pfind(*#{caller.Number}%b)")).IsEqualTo(await AsCaller("objid(me)"));
		await Assert.That(await AsCaller($"pfind(#%b{caller.Number}%b)")).IsEqualTo(await AsCaller("objid(me)"));
		await Assert.That(await AsCaller("pfind(*#0)")).IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(await AsCaller($"pfind(*#{caller.Number})")).IsEqualTo(await AsCaller("objid(me)"));
		await Assert.That(await AsCaller($"pfind({token}_Nobody)")).IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(Notifications.CountFor(caller)).IsEqualTo(before);

		await AsCaller($"pmatch({token}_Nobody)");
		await Assert.That(Notifications.CountFor(caller)).IsGreaterThan(before);
	}

	/// <summary>
	/// subeval() leaves what sits between brackets as written, does not compress spaces, and reads the caller's
	/// registers.
	/// </summary>
	[Test]
	[Arguments("subeval(lit([%#] [\\x] [a[b]%#]))", "[%#] [\\x] [a[b]%#]")]
	[Arguments("subeval(a[space(3)]b)", "a   b")]
	[Arguments("[subeval([space(2)]x)]|", "  x|")]
	[Arguments("[setq(0,hi)][subeval(lit(%q0 %q0))]", "hi hi")]
	[Arguments("subeval(lit(a\\ b%b%bc))", "a b  c")]
	[Arguments("subeval(lit([a\\]%#] [x%] %#]))", "[a\\]%#] [x%] %#]")]
	[Arguments("subeval(\\[[chr(123)]\\]\\} \\%#\\])", "[{]} %#]")]
	[Arguments("subeval(a [chr(123)]\\%#)", "a {#1}")]
	[Arguments("subeval(a \\[b [chr(123)]c)", "a [b {c}]")]
	[Arguments("[subeval(lit(%q<1 >))]|", "|")]
	[Arguments("subeval(lit(%b%ba%b%bb%b%b))|", "  a  b  |")]
	[Arguments("subeval(lit({%#} add(%#,1)))", "{#1} add(#1,1)")]
	[Arguments("[subeval(lit(%q<[setq(z,LEAK)]>))][r(z)]", "%q<[setq(z,LEAK)]>")]
	[Arguments("[setq(0,\\%q<>)][subeval(%q0)]|", "|")]
	[Arguments("subeval(lit(%<a b> %*<c>))", "<a b> *<c>")]
	[Arguments("[setq(0,\\%q<f\\(x\\)> \\%=<a\\,b>)][subeval(%q0)]", "%q<f(x)> %=<a,b>")]
	[Arguments("[setq(0,abc\\%)][subeval(%q0)]|", "abc%|")]
	[Arguments("[setq(0,abc\\\\)][subeval(%q0)]|", "abc|")]
	[Arguments("[setq(0,\\{\\%)][subeval(%q0)]", "{%}")]
	[Arguments("subeval(lit([%q]%#]))", "[%q]%#]")]
	[Arguments("[setq(0,\\%\\q\\<\\a\\(\\b\\>)][subeval(%q0)]", "")]
	[Arguments("[setq(0,\\%\\q\\<\\a\\%\\>)][subeval(%q0)]", "")]
	[Arguments("[setq(0,\\%\\q\\[\\a\\b\\c\\])][subeval(%q0)]", "abc]")]
	[Arguments("[setq(0,\\a\\%\\q\\ \\b)][subeval(%q0)]", "ab")]
	[Arguments("[setq(0,\\[\\%\\v\\]\\x)][subeval(%q0)]", "[%v]x")]
	[Arguments("[setq(0,\\[\\%\\v\\{\\a\\]\\x)][subeval(%q0)]", "[%v{a]x}]")]
	[Arguments("[setq(0,\\a\\%\\=\\<)][subeval(%q0)]", "a")]
	[Arguments("[setq(0,\\[\\%\\q\\<\\a\\\\\\]\\b)][subeval(%q0)]", "[%q<a\\]b")]
	[Arguments("[setq(0,\\%\\i)][subeval(%q0)]", "")]
	[Arguments("[setq(ab,X)][setq(1,ab)][setq(0,\\%\\q\\<\\%\\q\\1\\>)][subeval(%q0)]|", "|")]
	public async Task SubevalMatchesTinyMux(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	/// <summary>subeval() keeps the colour of the text it is given.</summary>
	[Test]
	public async Task SubevalKeepsColour()
	{
		var result = (await Parser.FunctionParse(MString.Plain("subeval(ansi(r,red [lit(\\[x\\])]))")))!.Message!;
		var expected = (await Parser.FunctionParse(MString.Plain("ansi(r,red \\[x\\])")))!.Message!;
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo(expected.Render(MarkupFormat.Ansi));

		var substituted = (await Parser.FunctionParse(MString.Plain("subeval(ansi(r,lit(%b%bx)))")))!.Message!;
		var red = (await Parser.FunctionParse(MString.Plain("ansi(r,%b%bx)")))!.Message!;
		await Assert.That(substituted.Render(MarkupFormat.Ansi)).IsEqualTo(red.Render(MarkupFormat.Ansi));
	}

	/// <summary>subeval() runs the substitutions in the text it is given and leaves its function calls as text.</summary>
	[Test]
	public async Task SubevalRunsSubstitutionsOnly()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MuxSubeval")})");
		await Eval($"attrib_set({thing}/TEXT,lit(%# said [add(1,2)] {{x}} at %l))");

		await Assert.That(await Eval($"subeval(get({thing}/TEXT))"))
			.IsEqualTo($"{await Eval("%#")} said [add(1,2)] {{x}} at ");
	}

	/// <summary>The bool family keeps TinyMUX's own test when <c>mux_logic</c> makes and() read numbers.</summary>
	[Test]
	public async Task BoolFunctionsIgnoreMuxLogic()
	{
		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxLogic = true, TinyBooleans = true }
		});
		await Assert.That(await Eval("[and(1,abc)][andbool(1,abc)][or(abc)][orbool(abc)][cand(1,abc)][candbool(1,abc)]"))
			.IsEqualTo("010101");
	}

	[Test]
	public async Task TranslateWritesSoftcodeForType1()
		=> await Assert.That(await Eval("translate(ansi(r,red%[x%]),p)")).IsEqualTo(await Eval("decompose(ansi(r,red%[x%]))"));

	[Test]
	public async Task CaselessRegexAttributeListsTakeTwoArguments()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MuxReglattri")})");
		await Eval($"[attrib_set({thing}/CMD_GO,1)][attrib_set({thing}/CMD_LOOK,1)][attrib_set({thing}/PLAYER_DATA,1)]");

		await Assert.That(await Eval($"reglattri({thing},^cmd)")).IsEqualTo("CMD_GO CMD_LOOK");
		await Assert.That(await Eval($"reglattri({thing},_data$)")).IsEqualTo("PLAYER_DATA");
		await Assert.That(await Eval($"regnattri({thing},^cmd)")).IsEqualTo("2");
		await Assert.That(await Eval($"[attrib_set({thing}/CMD_42,1)][reglattri({thing},^cmd_\\\\d+$)]")).IsEqualTo("CMD_42");
		await Assert.That(await Eval($"reglattri({thing},go|look)")).IsEqualTo("CMD_GO CMD_LOOK");
		await Assert.That(await Eval($"reglattri({thing},*)")).IsEqualTo(ErrorMessages.Returns.RegexpInvalid);
	}

	[Test]
	public async Task SecsTakesAZoneAndDecimalPlaces()
	{
		await Assert.That(await Eval("secs(utc,3)")).Matches(@"^\d+\.\d{3}$");
		await Assert.That(await Eval("secs(local,0)")).Matches(@"^\d+$");
		await Assert.That(await Eval("secs(utc)")).Matches(@"^\d+$");
	}

	[Test]
	public async Task RandomFunctionsStayInRange()
	{
		var rolls = (await Eval("lrand(1,6,500)")).Split(' ').Select(int.Parse).ToArray();
		await Assert.That(rolls.Length).IsEqualTo(500);
		await Assert.That(rolls.All(roll => roll is >= 1 and <= 6)).IsTrue();

		var bins = (await Eval("distribute(1000,7)")).Split(' ').Select(int.Parse).ToArray();
		await Assert.That(bins.Length).IsEqualTo(7);
		await Assert.That(bins.Sum()).IsEqualTo(1000);

		await Assert.That(await Eval("lrand(1,6,9000)")).Matches(@"^(\d (?!$)){7999}\d$");
	}

	[Test]
	public async Task CommandListsReadDollarAndCaretAttributes()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxCmds");
		await Eval($"[setq(0,create({thing}))][attrib_set(%q0/CMD.HELLO,lit($Hello *:think hi))][attrib_set(%q0/LISTEN.WAVE,lit(^* waves:think wave))][attrib_set(%q0/NOTE,text)]");
		var dbref = await Eval($"num({thing})");

		await Assert.That(await Eval($"lcmds({dbref})")).IsEqualTo("hello *");
		await Assert.That(await Eval($"lcmds({dbref},|,^)")).IsEqualTo("* waves");
		await Assert.That(await Eval($"lattrcmds({dbref})")).IsEqualTo("CMD.HELLO");
		await Assert.That(await Eval($"lattrcmds({dbref}/NOTE)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task RoomsWithinFollowExitsByDepth()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("MuxRooms");
		var rooms = (await Eval($"[setq(0,num(dig({name}A)))][setq(1,num(dig({name}B)))][setq(2,num(dig({name}C)))][null(open({name}AB,%q1,%q0),open({name}BC,%q2,%q1),open({name}BA,%q0,%q1))]%q0 %q1 %q2"))
			.Split(' ');
		await Assert.That(rooms.Length).IsEqualTo(3);
		var (a, b, c) = (rooms[0], rooms[1], rooms[2]);

		await Assert.That(await Eval($"lrooms({a})")).IsEqualTo(b);
		await Assert.That(await Eval($"lrooms({a},2)")).IsEqualTo($"{b} {c}");
		await Assert.That(await Eval($"lrooms({a},2,0)")).IsEqualTo(c);
		await Assert.That(await Eval($"lrooms({a},,)")).IsEqualTo(b);
		await Assert.That(await Eval($"lrooms({a},2,,1)")).IsEqualTo(await Eval($"[objid({b})] [objid({c})]"));
		await Assert.That(await Eval($"lrooms({a},0)")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"lrooms({a},51)")).IsEqualTo(ErrorMessages.Returns.NumberOutOfRange);
		await Assert.That(await Eval($"lrooms(me)")).IsEqualTo(ErrorMessages.Returns.NotARoom);
	}

	[Test]
	public async Task DestroyMarksTheObjectForDestruction()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxDestroy");
		await Assert.That(await Eval($"[setq(0,create({thing}))][destroy(%q0)][hasflag(%q0,GOING)]")).IsEqualTo("1");
	}

	[Test]
	public async Task QueueFunctionsAreOffByDefault()
	{
		using var _ = QueueFunctions(false);

		await Assert.That(await Eval("trigger(me/NOTHING)")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
		await Assert.That(await Eval("verb(me)")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
	}

	[Test]
	public async Task TriggerQueuesTheAttributeQuietly()
	{
		using var _ = QueueFunctions(true);
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxTrigger");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxTriggered");
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = Notifications.CountFor(god);

		await Assert.That(await Eval($"[setq(0,create({thing}))][attrib_set(%q0/GO,lit(@pemit %#={marker} %0))][trigger(%q0/GO,arg)]"))
			.IsEqualTo(string.Empty);

		await Notifications.WaitForAsync(god, $"{marker} arg", startIndex: before);
		await Assert.That(Notifications.For(god).Skip(before).Any(line => line.Contains($"{thing} - Triggered."))).IsFalse();
	}

	[Test]
	public async Task TriggerQuietSwitchLeavesOutTheNotice()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxTriggerCmd");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxTriggerCmdRan");
		var god = WebAppFactoryArg.ExecutorDBRef;
		await Eval($"[setq(0,create({thing}))][attrib_set(%q0/GO,lit(@pemit %#={marker}))]");
		var before = Notifications.CountFor(god);

		await Cmd($"@trigger/quiet {thing}/GO");

		await Notifications.WaitForAsync(god, marker, startIndex: before);
		await Assert.That(Notifications.For(god).Skip(before).Any(line => line.Contains($"{thing} - Triggered."))).IsFalse();
	}

	[Test]
	public async Task VerbTellsTheActorWhatAttribute()
	{
		using var _ = QueueFunctions(true);
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxVerb");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxVerbed");
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = Notifications.CountFor(god);

		await Assert.That(await Eval($"[setq(0,create({thing}))][attrib_set(%q0/XTEST,{marker})][verb(%q0,me,XTEST,default)]"))
			.IsEqualTo(string.Empty);

		await Notifications.WaitForAsync(god, marker, startIndex: before);
	}

	/// <summary>The awhat is queued with registers of its own, so what it sets never reaches the expression that called verb().</summary>
	[Test]
	public async Task VerbQueuesTheActions()
	{
		using var _ = QueueFunctions(true);
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxVerbQueue");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxVerbQueued");
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = Notifications.CountFor(god);

		await Assert.That(await Eval(
				$"[setq(0,create({thing}))][attrib_set(%q0/ACT,lit(think [setq(z,leaked)]; @pemit %#={marker}))][verb(%q0,me,,,,,ACT)][r(z)]"))
			.IsEqualTo(string.Empty);

		await Notifications.WaitForAsync(god, marker, startIndex: before);
	}
}
