namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's list-position, character-position, character-set, bitmask, number-base and speech-parsing
/// functions, each giving RhostMUSH's answer.
/// </summary>
public class RhostTextFunctionTests : ServerTestBase
{
	[Test]
	[Arguments("iindex(this is a test,another cat and,3,%b)", "this is another cat and a test")]
	[Arguments("iindex(a|b|c,x,2,|)", "a|x|b|c")]
	[Arguments("iindex(a b c,x,4,%b)", "a b c")]
	[Arguments("iindex(a b c,x,0,%b)", "a b c")]
	[Arguments("iindex(,x,1,|)", "")]
	[Arguments("iindex(a b,x,1,)", "a b")]
	[Arguments("aiindex(,another test,1,|)", "another test")]
	[Arguments("aiindex(another test,a test,1,|)", "a test|another test")]
	[Arguments("aindex(this is a test,was and,1,%b)", "this was and is a test")]
	[Arguments("aindex(a b c,x,3,%b)", "a b c x")]
	[Arguments("aindex(a||c,x,2,|)", "a||x|c")]
	[Arguments("rindex(this is a test,was another,2,%b)", "this was another test")]
	[Arguments("rindex(this is weird,rather weird you know,3,%b)", "this is rather weird you know")]
	[Arguments("rindex(a|b|c|d,x|y,2,|)", "a|x|y|d")]
	[Arguments("rindex(a|b|c,,2,|)", "a|b|c")]
	public async Task IndexEdits(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("creplace(this is a test,4,whee)", "thiwhee a test")]
	[Arguments("creplace(this is a test,4,wheewheewhoowha!)", "thiwheewheewhoowha!")]
	[Arguments("creplace(this is a test,4,wheewheewhoowha!,c)", "thiwheewheewho")]
	[Arguments("creplace(this is a test,4,wheewheewhoowha!,i)", "thiwheewheewhoowha!s is a test")]
	[Arguments("creplace(this is a test,4,moo!,r)", "thimoo!")]
	[Arguments("creplace(abcdef,2,XYZ,o,2)", "aXYdef")]
	[Arguments("creplace(abcdef,2,XYZ,i,1)", "aXbcdef")]
	[Arguments("creplace(abcdef,5,XYZ,c)", "abcdXY")]
	[Arguments("creplace(abc,5,X)", "abcX")]
	[Arguments("creplace(abc,1,X,z)", "")]
	[Arguments("creplace(this is a test,1 3 4,X)", "XhXX is a test")]
	[Arguments("creplace(this is a test,1 3 4,XYZ)", "XhYZ is a test")]
	[Arguments("creplace(abc,1 9 2,XY)", "XYc")]
	[Arguments("creplace(abc,0,X)", "#-1 VALUE MUST BE > 0 < 3999")]
	[Arguments("creplace(abc,1 2,X,o)", "#-1 MULTI-REPLACE OPTION REQUIRES 3 ARGS ONLY.")]
	public async Task CharacterReplacement(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("numpos(t,this is a test)", "3")]
	[Arguments("numpos(aa,aaaa)", "3")]
	[Arguments("numpos(x,abc)", "0")]
	[Arguments("numpos(st,this is a test,1)", "6")]
	[Arguments("numpos(,abc)", "3")]
	[Arguments("numpos(t,ansi(r,this is a test),0,1)", "3")]
	public async Task MatchCounts(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("andchr(abc,ca)", "1")]
	[Arguments("andchr(abc,aa)", "0")]
	[Arguments("andchr(abc,aa,1)", "1")]
	[Arguments("andchr(abc,)", "1")]
	[Arguments("orchr(abc,xya)", "1")]
	[Arguments("orchr(abc,xyz)", "0")]
	[Arguments("orchr(abc,)", "0")]
	[Arguments("orchr(\U0001F600,\U0001F601)", "0")]
	[Arguments("andchr(x\U0001F600,\U0001F600)", "1")]
	[Arguments("xorchr(abc,ax)", "1")]
	[Arguments("xorchr(abc,ab)", "0")]
	[Arguments("xorchr(abc,aa)", "1")]
	[Arguments("xorchr(abc,aa,1)", "0")]
	[Arguments("notchr(abc,xyz)", "1")]
	[Arguments("notchr(abc,c)", "0")]
	[Arguments("notchr(abc,)", "1")]
	public async Task CharacterSets(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mask(12,10)", "8")]
	[Arguments("mask(12,10,6,&)", "0")]
	[Arguments("mask(1,2,4,|)", "7")]
	[Arguments("mask(12,10,^)", "6")]
	[Arguments("mask(12,10,~)", "4")]
	[Arguments("mask(5,0,1)", "4294967290")]
	[Arguments("mask(5,0,2)", "4294967291")]
	[Arguments("mask(12,10,?)", "8")]
	[Arguments("mask(-1,3)", "#-1")]
	[Arguments("mask(3,-1)", "#-1")]
	[Arguments("mask(a,3)", "#-1")]
	public async Task Bitmasks(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("tobin(5)", "00000000000000000000000000000101")]
	[Arguments("tobin(-1)", "11111111111111111111111111111111")]
	[Arguments("tohex(255)", "FF")]
	[Arguments("tohex(-1)", "FFFFFFFF")]
	[Arguments("tohex(0x1f)", "1F")]
	[Arguments("tohex(0b1111)", "F")]
	[Arguments("tooct(8)", "10")]
	[Arguments("tooct(010)", "10")]
	[Arguments("todec(0x1F)", "31")]
	[Arguments("todec(0b101)", "5")]
	[Arguments("todec(-1)", "4294967295")]
	[Arguments("todec(12abc)", "12")]
	[Arguments("todec(abc)", "0")]
	public async Task NumberBases(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("parsestr(You say \"Hello! Hello?\" *sigh* \"Hello?!?\",X,\")", "You say \"X\" *sigh* \"X\"")]
	[Arguments("parsestr(You say \"Hello! Hello?\" *sigh* \"Hello?!?\",X,\",,1)", "X\"Hello! Hello?\"X\"Hello?!?\"X")]
	[Arguments("parsestr(a \"b\" c \"d\",%0:%2:%3,\")", "a \"b:0:1\" c \"d:1:3\"")]
	[Arguments("parsestr(a \"b\" c,<%0>,\",<< >>)", "a <<<b>>> c")]
	[Arguments("parsestr(one two three,\\[%0\\])", "one [two] three")]
	[Arguments("parsestr(x \"+a\" y \"b\",<%0>,\",,,,+,,T%0%4)", "x \"T+a+\" y \"<b>\"")]
	[Arguments("parsestr(x \"a!\" y \"b\",<%0>,\",,,,,!,E%0%4)", "x \"Ea!!\" y \"<b>\"")]
	[Arguments("parsestr(\"hi there,<%0>,\",,,&Bob)", "Bob says, \"<hi there>\"")]
	[Arguments("parsestr(hi there,<%0>,\",,,&Bob)", "Bob says, \"<hi there>\"")]
	[Arguments("parsestr(:waves \"hi\",<%0:%1>,\",,,&Bob)", "Bob waves \"<hi:Bob>\"")]
	[Arguments("parsestr(;'s hat,<%0>,\",,,&Bob)", "Bob's hat")]
	[Arguments("parsestr(|The wind \"howls\",<%0>,\",,,&Bob,,,,,OOC:%b)", "OOC: The wind \"<howls>\"")]
	[Arguments("parsestr(\"hi,<%0>,\",,,&Bob,,,,whispers,)", "Bob whispers \"<hi>\"")]
	[Arguments("parsestr(abc,)", "abc")]
	public async Task SpeechParsing(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("str(This is a test,his)", "his is a test")]
	[Arguments("str(This is a test,HIS)", "#-1")]
	[Arguments("totmember(This is a totmember test this is,this)", "6")]
	[Arguments("totmember(a b a c a,a)", "1 3 5")]
	[Arguments("totmember(a|b|A,a,|)", "1")]
	[Arguments("totmember(a b c,d)", "0")]
	[Arguments("totpos(t,this is a test)", "1 11 14")]
	[Arguments("totpos(aa,aaaa)", "1 2 3")]
	[Arguments("totpos(st,this is a test,1)", "1 4 7 11 13 14")]
	[Arguments("totpos(st,this is a test,2)", "tsstst")]
	[Arguments("totpos(x,abc)", "")]
	[Arguments("totpos(\U0001F600,\U0001F601)", "")]
	[Arguments("totpos(\U0001F600b,\U0001F600ab,1)", "1 4")]
	[Arguments("totpos(\U0001F600,a\U0001F600,2)", "\U0001F600")]
	[Arguments("securex(a\\[b\\]%%c\\,d)", "a b  c d")]
	[Arguments("securex(a\\[b\\]%%c\\,d,\\[%%)", "a[b %c d")]
	public async Task RhostStringShims(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task ParentsLeaveOutTheObject()
	{
		var child = await Eval("create(rhost parents child)");
		var parent = await Eval("create(rhost parents parent)");
		var grandparent = await Eval("create(rhost parents grandparent)");
		await Eval($"[parent({child},{parent})][parent({parent},{grandparent})]");

		await Assert.That(await Eval($"parents({child})")).IsEqualTo($"{parent} {grandparent}");
		await Assert.That(await Eval($"parents({grandparent})")).IsEqualTo("#-1");
		await Assert.That(await Eval($"parents({child},1)")).IsEqualTo(await Eval($"[objid({parent})] [objid({grandparent})]"));
		await Assert.That(await Eval("parents(#-5)")).IsEqualTo("#-1");
	}

	[Test]
	public async Task SpeechParsingKeepsColour()
	{
		await Assert.That(await Ansi("parsestr(ansi(r,a \"b\" c),<%0>,\")")).IsEqualTo(await Ansi("[ansi(r,a%b)]\"<[ansi(r,b)]>\"[ansi(r,%bc)]"));
		await Assert.That(await Ansi("parsestr(ansi(r,hi),%0,\",,,&Bob)")).IsEqualTo(await Ansi("Bob says, \"[ansi(r,hi)]\""));
	}

	[Test]
	public async Task StringShimsKeepColour()
	{
		await Assert.That(await Ansi("securex(ansi(r,a\\[b))")).IsEqualTo(await Ansi("ansi(r,a b)"));
		await Assert.That(await Ansi("str(ansi(r,abc)def,cd)")).IsEqualTo(await Ansi("[ansi(r,c)]def"));
	}

	[Test]
	public async Task PointsDoNotMoveTheCut()
	{
		await Assert.That(await Ansi("parsestr([beep()]:waves,%0,,,,&Bob)")).IsEqualTo(await Ansi("Bob [beep()]waves"));
		await Assert.That(await Ansi("parsestr(;[beep()] grins,%0,,,,&Bob)")).IsEqualTo(await Ansi("Bob [beep()]grins"));
		await Assert.That(await Eval("parsestr(a [beep()] b,<%0>)")).IsEqualTo("a <b>");
		await Assert.That(await Ansi("str(x[beep()]abc,abc)")).IsEqualTo("abc");
		await Assert.That(await Ansi("str(ab[beep()]cd,bc)")).IsEqualTo(await Ansi("b[beep()]cd"));
	}

	[Test]
	public async Task SpeechParsingStopsAtTheOutputLimit()
	{
		var result = await Eval("parsestr(repeat(a|,200),repeat(x,100000),|)");
		await Assert.That(result.Length).IsLessThan(1_000_000);
		await Assert.That(result).StartsWith("#-1");
	}

	/// <summary>Large inputs answer in one pass: these would run for minutes if each step rescanned the input.</summary>
	[Test]
	[Arguments("orchr(repeat(a,500000),repeat(b,500000))", "0")]
	[Arguments("andchr(repeat(a,500000),repeat(a,500000))", "1")]
	[Arguments("strlen(creplace(repeat(a,500000),lnum(1,100000),X))", "500000")]
	[Arguments("numpos(repeat(a,2000)b,repeat(a,500000))", "0")]
	[Arguments("words(totpos(repeat(a,2000),repeat(a,500000)))", "498001")]
	[Arguments("strlen(securex(repeat(\\[,500000),repeat(x,500000)))", "500000")]
	public async Task LargeInputsStayLinear(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task SpeechParsingNamesTheTarget()
	{
		await Assert.That(await Eval("parsestr(\"hi,<%0>,\",,,#1)")).IsEqualTo(await Eval("name(#1)") + " says, \"<hi>\"");
	}

	private async Task<string> Ansi(string code)
		=> (await Parser.FunctionParse(MString.Plain(code)))!.Message.Render(MarkupString.MarkupFormat.Ansi);
}
