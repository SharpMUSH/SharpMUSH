using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The examples in RhostMUSH's help for the functions SharpMUSH takes in, run the way an imported RhostMUSH game
/// runs them: through the import's rewrite and under the options the import turns on. Where the help's own answer
/// is wrong, the row carries the right one and says why. Examples that need a random or timed answer, a
/// configured reality level, or another object in a known place are covered by the per-function test classes.
/// </summary>
public class RhostHelpExampleTests : ServerTestBase
{
	private static IDisposable RhostOptions() => TestOptionsOverride.Scope(configured => configured with
	{
		Compatibility = configured.Compatibility with
		{
			ParenGroups = true,
			TinyTrimFun = true,
			RhostEdit = true,
			RhostLattr = true,
			RhostPrintf = true,
			RhostMapArgs = true,
			RhostList = true,
			RhostIterSeparator = true,
			RhostLrooms = true,
			Dialect = SoftcodeDialects.Rhost.Key
		}
	});

	private async Task<string> RhostEval(string code)
	{
		using var scope = RhostOptions();
		return await Eval(RhostSoftcodeRewriter.Rewrite(code).Text);
	}

	private async Task<string> RhostEvalAs(DBRef executor, string code)
	{
		using var scope = RhostOptions();
		return await EvalAs(executor, RhostSoftcodeRewriter.Rewrite(code).Text);
	}

	private Task<DBRef> CreateThingAsync(string name) =>
		TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, name);

	/// <summary>between(), fbetween() and ncomp().</summary>
	[Test]
	[Arguments("between(10,15,14)", "1")]
	[Arguments("between(10,15,15)", "0")]
	[Arguments("between(10,15,15,1)", "1")]
	[Arguments("fbetween(10.01,15.01,14.5)", "1")]
	[Arguments("fbetween(10.5,15.20,15.20)", "0")]
	[Arguments("fbetween(10.5,15.20,15.20,1)", "1")]
	[Arguments("ncomp(2,1)", "1")]
	[Arguments("ncomp(1,2)", "-1")]
	[Arguments("ncomp(2,2)", "0")]
	[Arguments("ncomp(1.1,1.2)", "-1")]
	public async Task Comparisons(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>isalnum(), isalpha(), isdigit(), ispunct(), isspace(), isxdigit(), istag() and isdbref().</summary>
	[Test]
	[Arguments("isalnum(?)", "0")]
	[Arguments("isalnum(9********)", "1")]
	[Arguments("isalpha(a1238548385**|%*)", "1")]
	[Arguments("isalpha(0)", "0")]
	[Arguments("isdigit(What?)", "0")]
	[Arguments("isdigit(1)", "1")]
	[Arguments("ispunct(14789asdf!!!!!..???)", "0")]
	[Arguments("ispunct(?hurm?)", "1")]
	[Arguments("isspace(space(5))", "1")]
	[Arguments("isspace(%r)", "1")]
	[Arguments("isspace(1)", "0")]
	[Arguments("istag(#-1)", "0")]
	[Arguments("isdbref(#sirnotappearinginthismovie)", "0")]
	[Arguments("isxdigit(freep?)", "1")]
	[Arguments("isxdigit(G)", "0")]
	public async Task CharacterTests(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>The l-prefixed list arithmetic and logic functions.</summary>
	[Test]
	[Arguments("ladd(1 1 1 1 1)", "5")]
	[Arguments("ladd(1@1@1@1@1,@)", "5")]
	[Arguments("land(0 1 1)", "0")]
	[Arguments("land(0@1@1,@)", "0")]
	[Arguments("lavg(2 4 6 8 10)", "6")]
	[Arguments("lavg(2@4@6@8@10,@)", "6")]
	[Arguments("ldiv(20 2 0 0 0 0 2)", "5")]
	[Arguments("ldiv(20@2@0@0@0@0@2,@)", "5")]
	[Arguments("lmax(1 10 2 4 6)", "10")]
	[Arguments("lmax(1@10@2@4@6,@)", "10")]
	// lmin()'s help calls lmax() in its examples.
	[Arguments("lmin(1 10 2 -4 6)", "-4")]
	[Arguments("lmin(1@10@2@-4@6,@)", "-4")]
	[Arguments("lmul(2 2 2 2)", "16")]
	[Arguments("lmul(2@2@2@2,@)", "16")]
	[Arguments("lnor(0 0)", "1")]
	[Arguments("lnor(0@0,@)", "1")]
	[Arguments("lor(0 1 1)", "1")]
	[Arguments("lor(0@1@1,@)", "1")]
	[Arguments("lsub(10 2 3 1)", "4")]
	[Arguments("lsub(10@2@3@1,@)", "4")]
	[Arguments("lxnor(1 1 1 1 1 0)", "0")]
	[Arguments("lxnor(1@1@1@1@1@0,@)", "0")]
	[Arguments("lxor(0 1 0 1 0 1)", "1")]
	[Arguments("lxor(0@1@0@1@0@1,@)", "1")]
	public async Task ListMath(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>listdiff(), listinter() and listunion().</summary>
	[Test]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep)", "foo baz")]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep,,-)", "foo-baz")]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep,,-,1)", "foo-baz-foo-foo")]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep)", "gleep bar")]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep,,-)", "gleep-bar")]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep,,-,1)", "gleep-bar-bar")]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep)", "foo baz gleep bar moof")]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep,,-)", "foo-baz-gleep-bar-moof")]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep,,-,1)", "foo-baz-gleep-bar-foo-bar-moof")]
	public async Task ListSets(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>citer(), nsiter() and iter().</summary>
	[Test]
	[Arguments("citer(boo!,##)", "b o o !")]
	[Arguments("citer(boo!,##-#@)", "b-1 o-2 o-3 !-4")]
	[Arguments("citer(testing,strlen(##))", "1 1 1 1 1 1 1")]
	[Arguments("citer(bob,## WHEE ##,@)", "b WHEE b@o WHEE o@b WHEE b")]
	[Arguments("iter(This is a test,-## #@-)", "-This 1- -is 2- -a 3- -test 4-")]
	[Arguments("iter(This is a test,strlen(##))", "4 2 1 4")]
	[Arguments("iter(lnum(10),mul(mul(##,##),10))", "0 10 40 90 160 250 360 490 640 810")]
	[Arguments("iter(Was it a cat I saw,words(##),s)", "1 4 1")]
	[Arguments("iter(This is a test,##,,***)", "This***is***a***test")]
	[Arguments("iter(This is a test,%i0,,***)", "This***is***a***test")]
	[Arguments("iter(:1000:>:,%i0)", ":1000:>:")]
	// The help shows a fourth item that lnum(3) does not give.
	[Arguments("nsiter(lnum(3),--+#@)", "--+1--+2--+3")]
	[Arguments("nsiter(lnum(4),--+)", "--+--+--+--+")]
	[Arguments("nsiter(This is a test,strlen(##))", "4214")]
	[Arguments("nsiter(lnum(10),mul(mul(##,##),10))", "0104090160250360490640810")]
	[Arguments("nsiter(Was it a cat I saw,words(##),s)", "141")]
	public async Task Loops(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>crc32(), pack(), unpack(), roman(), power10() and the hyperbolic functions.</summary>
	[Test]
	[Arguments("crc32(MUX)", "629126998")]
	[Arguments("pack(629126998)", "bVxTM")]
	[Arguments("pack(629126998,16)", "257FB756")]
	[Arguments("pack(629126998,2)", "100101011111111011011101010110")]
	[Arguments("pack(-629126998)", "-bVxTM")]
	[Arguments("unpack(bVxTM)", "629126998")]
	[Arguments("unpack(1111,2)", "15")]
	[Arguments("moon(999999999)", "The Moon is Waning Gibbous (72% of Full)")]
	[Arguments("roman(1999)", "MCMXCIX")]
	[Arguments("roman(777777)", "dcclxxvMMDCCLXXVII")]
	[Arguments("power10(2)", "100")]
	[Arguments("power10(5)", "100000")]
	[Arguments("cosh(0)", "1")]
	// fdiv(pi(),2) is 1.570796, as C writes it; the help's answers are for pi/2 itself.
	[Arguments("cosh(fdiv(pi(),2))", "2.509178")]
	[Arguments("cosh(fdiv(pi(),4))", "1.324609")]
	[Arguments("cosh(fdiv(pi(),6))", "1.140238")]
	[Arguments("sinh(0)", "0")]
	[Arguments("sinh(fdiv(pi(),2))", "2.301298")]
	[Arguments("sinh(fdiv(pi(),4))", "0.868671")]
	[Arguments("sinh(fdiv(pi(),6))", "0.547854")]
	[Arguments("tanh(0)", "0")]
	[Arguments("tanh(1)", "0.761594")]
	// The help shows cosh()'s answer here.
	[Arguments("tanh(fdiv(pi(),4))", "0.655794")]
	public async Task Numbers(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>elementpos(), strip(), strdistance(), strfunc(), ljc(), rjc(), nummatch(), chomp(), edit() and trim().</summary>
	[Test]
	[Arguments("elementpos(abcdefg,1 4)", "a d")]
	[Arguments("elementpos(abcdefg,1 4 7,)", "adg")]
	[Arguments("strip(This is a test,is)", "Th  a tet")]
	[Arguments("strip(abcdefgABCDEFG,azGy!7$B)", "bcdefgACDEF")]
	[Arguments("strip(This is a test,is,1)", "isiss")]
	[Arguments("[strdistance(this,thiz)]", "1")]
	[Arguments("[strdistance(this,This)]", "1")]
	[Arguments("[strdistance(this,This,1)]", "0")]
	[Arguments("[strdistance(the,teh)]", "2")]
	[Arguments("[strdistance(the,teh,,1)]", "1")]
	[Arguments("add(1,2,3,4)", "10")]
	[Arguments("strfunc(add,1 2 3 4)", "10")]
	[Arguments("strfunc(add,1@2@3@4,@)", "10")]
	[Arguments("hastype(me,player)", "1")]
	[Arguments("strfunc(hastype,me player)", "1")]
	[Arguments("switch(add(1,1),2,match,1,nope,0,nope,nope)", "match")]
	[Arguments("strfunc(switch,add(1,1) 2 match 1 nope 0 nope nope)", "match")]
	[Arguments("-[ljc(foo,6)]-", "-foo   -")]
	[Arguments("-[ljc(this is foo,6)]-", "-this i-")]
	[Arguments("-[rjc(foo,6)]-", "-   foo-")]
	[Arguments("-[rjc(this is foo,6)]-", "-this i-")]
	[Arguments("nummatch(This is a test, test)", "1")]
	[Arguments("nummatch(This test is a test of nummatch.  A test is fun, test)", "3")]
	[Arguments("nummatch(This test is silly, fred)", "0")]
	[Arguments("chomp(%rthis is a test%r)", "\nthis is a test\n")]
	[Arguments("chomp(%rthis is a test%r,l)", "\nthis is a test\n")]
	[Arguments("chomp(%rthis is a test%r,r)", "\nthis is a test\n")]
	[Arguments("edit(This is a test,is,x)", "Thx x a test")]
	[Arguments("edit(Atlantic,^,Trans)", "TransAtlantic")]
	[Arguments("trim(;;;Wacka;;;,,;)", "Wacka")]
	[Arguments("trim(%b%b%b Polly Parrot %b%b%b%b,r)", "    Polly Parrot")]
	[Arguments("trim(---Trim Rules!---,l,-)", "Trim Rules!---")]
	public async Task Strings(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>setqm(), setrm(), nameq() and nameqm().</summary>
	[Test]
	[Arguments("[setqm(0,val1,1,val2)][nameqm(0,foolabel,1,barlabel)]%q<foolabel>", "val1")]
	[Arguments("[setqm(0,foo,1,bar,2,%q0%q1baz)]%q2", "foobarbaz")]
	[Arguments("[setqm(label1,moo,label2,cow)]%q<label1> goes the %q<label2>.", "moo goes the cow.")]
	// The help's answers for these four end in a period the code does not write.
	[Arguments("[setqm(a>label1,moo,5>label2,cow)]%q<a> goes the %q<5>", "moo goes the cow")]
	[Arguments("[setqm(a>label1,moo,5>label2,cow)]%q<label1> goes the %q<label2>", "moo goes the cow")]
	[Arguments("[setrm(0,foo,1,bar,2,%q0%q1baz,null())] Reg:%q2", "foobarfoobarbaz Reg:foobarbaz")]
	[Arguments("[setrm(0,foo,1,bar,2,%q0%q1baz)] Reg:%q2", "foo bar foobarbaz Reg:foobarbaz")]
	[Arguments("[setrm(a>label1,moo,5>label2,cow)]//%q<a> goes the %q<5>", "moo cow//moo goes the cow")]
	[Arguments("[setrm(a>label1,moo,5>label2,cow,++)]//%q<label1> goes the %q<label2>", "moo++cow//moo goes the cow")]
	[Arguments("[setq(0,Test 1 2 3)][r(0)]", "Test 1 2 3")]
	[Arguments("[setq(0,Test 1 2 3)][nameq(0,Foo Foo)][r(Foo Foo)]", "Test 1 2 3")]
	[Arguments("[setq(0,Test 1 2 3)][nameq(0,Foo Foo)][nameq(0)]", "Foo Foo")]
	[Arguments("[setq(0,Test 1 2 3)][nameq(0,Foo Foo)][nameq(Foo Foo,,1)]", "0")]
	// Register names are reported in upper case (the help shows lower case).
	[Arguments("[setq(a,store,foo)][nameq(foo,,1)]", "A")]
	[Arguments("[setq(0,foo)][setq(a,,bar)][nameq(*,,U)]", "0 A")]
	[Arguments("[setq(0,foo)][setq(a,,bar)][nameq(*,,V)]", "0")]
	[Arguments("[setq(0,foo)][setq(a,,bar)][nameq(*,,L)]", "A")]
	public async Task Registers(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>lnum2(), xnor(), andflag(), orflag() and moneyname(), which the import rewrites.</summary>
	[Test]
	[Arguments("lnum2(5)", "0 1 2 3 4")]
	[Arguments("lnum2(10,5,+)", "5 6 7 8 9 10 11 12 13 14")]
	[Arguments("lnum2(10,5,+,2)", "5 7 9 11 13 15 17 19 21 23")]
	[Arguments("lnum2(10,20,-)", "20 19 18 17 16 15 14 13 12 11")]
	[Arguments("[xnor(1,0)] @ [xnor(0,0)] @ [xnor(0,1)] @ [xnor(1,1)]", "0 @ 1 @ 0 @ 1")]
	[Arguments("andflag(me,ansi,terse,going)", "0")]
	[Arguments("orflag(me,slave,fubar,dark,unf)", "0")]
	[Arguments("moneyname()", "Penny")]
	[Arguments("moneyname(0)", "Pennies")]
	[Arguments("50 [moneyname(50)]", "50 Pennies")]
	public async Task Conversions(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>globalroom(), keepflags() and remflags().</summary>
	[Test]
	[Arguments("globalroom()", "#2")]
	[Arguments("keepflags(#1 #2 #3 #4,&W)", "#1")]
	[Arguments("keepflags(#1 #2 #3 #4,&Ws)", "")]
	[Arguments("keepflags(#1 #2 #3 #4,|Ws)", "#1")]
	[Arguments("remflags(#1 #2 #3 #4,&W)", "#2 #3 #4")]
	[Arguments("remflags(#1 #2 #3 #4,&Ws)", "#1 #2 #3 #4")]
	[Arguments("remflags(#1 #2 #3 #4,|Ws)", "#2 #3 #4")]
	public async Task Objects(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>printf() in place of center(), ljust() and rjust().</summary>
	[Test]
	[Arguments("center(- test -,70)", "                               - test -                               ")]
	[Arguments("printf($^70s,- test -)", "                               - test -                               ")]
	[Arguments("center(- test -,70,-)", "-------------------------------- test --------------------------------")]
	[Arguments("printf($^70:-:s,- test -)", "-------------------------------- test --------------------------------")]
	[Arguments("center(- test -,4,-)", "- test -")]
	[Arguments("printf($^4+:-:s,- test -)", "- test -")]
	[Arguments("ljust(- test -,70)", "- test -                                                              ")]
	[Arguments("printf($-70s,- test -)", "- test -                                                              ")]
	[Arguments("ljust(- test -,70,-)", "- test ---------------------------------------------------------------")]
	[Arguments("printf($-70:-:s,- test -)", "- test ---------------------------------------------------------------")]
	[Arguments("ljust(- test -,4,-)", "- test -")]
	[Arguments("printf($-4+:-:s,- test -)", "- test -")]
	[Arguments("rjust(- test -,70)", "                                                              - test -")]
	[Arguments("printf($70s,- test -)", "                                                              - test -")]
	[Arguments("rjust(- test -,70,-)", "--------------------------------------------------------------- test -")]
	[Arguments("printf($70:-:s,- test -)", "--------------------------------------------------------------- test -")]
	[Arguments("rjust(- test -,4,-)", "- test -")]
	[Arguments("printf($4+:-:s,- test -)", "- test -")]
	public async Task PrintfSubstitutes(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>printf()'s field codes.</summary>
	[Test]
	[Arguments("printf($.1:|:&s $-10|\"s $1.:|:&s, %r, test, %r)", "| test       |")]
	[Arguments("printf($.1:|:&s $-10|\"s $1.:|:&s, %r, test1 test2, %r)", "| test1      |\n| test2      |")]
	[Arguments("printf($.1:|:&s $-10|\"s $1.:|:&s, %r, test1 test2 test3, %r)", "| test1      |\n| test2      |\n| test3      |")]
	[Arguments("printf($.7:|+=:&s $-10|\"s $7.:|+=:&s, %r, test1 test2 test3, %r)", "|+=|+=| test1      |+=|+=|\n|+=|+=| test2      |+=|+=|\n|+=|+=| test3      |+=|+=|")]
	[Arguments("1[printf($10&s $10&s, %r, %r)]2", "1                     \n                     2")]
	// Two ten-wide fields and the space between them are 21 columns; the help shows 20.
	[Arguments("1[printf($10&.s $10&.s, %r, %r)]2", "1                     2")]
	[Arguments("1[printf($10!s $10!s, one, two)]2", "1       one        two2")]
	[Arguments("1[printf($10!s $10!s, , two)]2", "1       two2")]
	// A live RhostMUSH 5.11.2 keeps the space before an empty last field; the help drops it.
	[Arguments("1[printf($10!s $10!s, one, )]2", "1       one 2")]
	[Arguments("1[printf($10!s $10!s, , )]2", "12")]
	[Arguments("1[printf($10@s $10@s, one, two)]2", "1       one        two2")]
	[Arguments("1[printf($10@s $10@s, , two)]2", "1       two2")]
	[Arguments("1[printf($10@s $10@s, one, )]2", "1       one           2")]
	[Arguments("1[printf($10@s $10@s, , )]2", "12")]
	[Arguments("1[printf($10s $10<s, one, two)]2", "1       one        two2")]
	[Arguments("1[printf($10s $10<s, , two)]2", "1                     2")]
	[Arguments("1[printf($10s $10<s, one, )]2", "1       one           2")]
	[Arguments("1[printf($10s $10<s, , )]2", "1                     2")]
	[Arguments("1[printf($10>s $10s, one, two)]2", "1       one        two2")]
	[Arguments("1[printf($10>s $10s, , two)]2", "1                  two2")]
	[Arguments("1[printf($10>s $10s, one, )]2", "1                     2")]
	[Arguments("1[printf($10>s $10s, , )]2", "1                     2")]
	[Arguments("1[printf($10s $10s, one, two)]2", "1       one        two2")]
	[Arguments("1[printf($-10s $-10s, one, two)]2", "1one        two       2")]
	[Arguments("1[printf($^10s $^10s, one, two)]2", "1   one        two    2")]
	[Arguments("1[printf($40s,this is a test)]2", "1                          this is a test2")]
	[Arguments("1[printf($_40s,this is a test)]2", "1this          is          a         test2")]
	[Arguments("1[printf($10s,this is a test)]2", "1this is a 2")]
	[Arguments("1[printf($10+s,this is a test)]2", "1this is a test2")]
	[Arguments("1[printf($10*s,this is a test)]2", "1 is a test2")]
	[Arguments("[printf($|10s,this is wrapping the text)]", "this is wr\napping the\n      text")]
	[Arguments("[printf($-|\"10s,this is wrapping the text)]", "this is   \nwrapping  \nthe text  ")]
	[Arguments("printf($&-10s$&-68s, 1%r2%r3%r4, A%rB%rC%rD%rE%rF)", "1         A                                                                   \n2         B                                                                   \n3         C                                                                   \n4         D                                                                   \n          E                                                                   \n          F                                                                   ")]
	[Arguments("printf($&-10's$&-68s, 1%r2%r3%r4, A%rB%rC%rD%rE%rF)", "1         A                                                                   \n2         B                                                                   \n3         C                                                                   \n4         D                                                                   \nE                                                                             \nF                                                                             ")]
	[Arguments("printf($&-10s$&-10s, 1%r2%r3%r4%r5%r6, A%rB%rC%rD)x", "1         A         \n2         B         \n3         C         \n4         D         \n5                   \n6                   x")]
	[Arguments("printf($&-10s$&`-10s, 1%r2%r3%r4%r5%r6, A%rB%rC%rD)x", "1         A         \n2         B         \n3         C         \n4         D         \n          5         \n          6         x")]
	[Arguments("[printf($-40:.:s,one char filler)]", "one char filler.........................")]
	[Arguments("[printf($-40:0123456789:s,ten char filler)]", "ten char filler5678901234567890123456789")]
	[Arguments("[printf($40:0123456789:s,ten char filler)]", "0123456789012345678901234ten char filler")]
	[Arguments("[printf($^40:0123456789:s,ten char filler)]", "012345678901ten char filler7890123456789")]
	[Arguments("[printf($_40:0123456789:s,ten char filler)]", "ten34567890123456char1234567890123filler")]
	[Arguments("[printf($-40/100/\"|s,repeat(a%b,200))]", "a a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a                     ")]
	[Arguments("[printf($-40/100/\"|s,repeat(a%b,20000))]", "a a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a                     ")]
	[Arguments("[printf($-40\"|s,repeat(a%b,50))]", "a a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a                     ")]
	[Arguments("[printf($.-40/w1/\"|s,repeat(a%b,20000))]", "a a a a a a a a a a a a a a a a a a a a ")]
	[Arguments("[printf($-40/w2/\"|s,repeat(a%b,20000))]", "a a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a a a a a a a a a a a ")]
	[Arguments("[printf($-40/w3/\"|s,repeat(a%b,20000))]", "a a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a a a a a a a a a a a \na a a a a a a a a a a a a a a a a a a a ")]
	[Arguments("[printf($-40#s, a%tb%tc%td%te)]", "a    b    c    d    e                   ")]
	[Arguments("[printf($-40#2#s, a%tb%tc%td%te)]", "a  b  c  d  e                           ")]
	[Arguments("[printf($-40#7#s, a%tb%tc%td%te)]", "a       b       c       d       e       ")]
	// The help shows a four-space indent for ;5;.
	[Arguments("[printf($-20\"|;5;s,this is a test with wrapping some text)]", "this is a test with \n     wrapping some  \n     text           ")]
	[Arguments("[printf($-20\"|;5.1;s,this is a test with wrapping some text)]", "this is a test with \nwrapping some       \n     text           ")]
	public async Task PrintfCodes(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>printf()'s worked examples.</summary>
	[Test]
	[Arguments("printf($s,simple test)", "simple test")]
	[Arguments("printf(this is a test: |$-15s|,test 1 2 3)", "this is a test: |test 1 2 3     |")]
	[Arguments("printf(this is a test: |$&-15s|,test %r1 %r2 %r3)", "this is a test: |test           |\n                 1              \n                 2              \n                 3              ")]
	[Arguments("printf($15:x:sx$-60:x:s,Walker,Staff & Developer)", "xxxxxxxxxWalkerxStaff & Developerxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
	[Arguments("printf($15:x:sx$s,Walker,Staff & Developer)", "xxxxxxxxxWalkerxStaff & Developer")]
	public async Task PrintfExamples(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	/// <summary>columns().</summary>
	[Test]
	[Arguments("columns(this is a test of columns,10,3)", "this      a         of        \nis        test      columns   ")]
	[Arguments("columns(this is a test of columns,10,3,L,,,,,,1)", "this      is        a         \ntest      of        columns   ")]
	[Arguments("columns(this is a test of columns,10,3,R)", "      this         a        of\n        is      test   columns")]
	[Arguments("columns(this is a test of columns,10,3,R,0)", "      this         a        of\n        is      test   columns")]
	[Arguments("columns(this is a test of columns,10,3,R,1)", "   this      a     of\n     is   testcolumns")]
	[Arguments("columns(this is a test of columns,3,3,R,0,0)", "thitescol\n  s  tumn\n is of  s\n  a")]
	[Arguments("columns(this is a test of columns,3,3,R,0,1)", "thi  a of\n istescol")]
	[Arguments("columns(this is a test of columns,10,3,R,0,0,<*,|,*>,0)", "<*      this|         a|        of*>\n<*        is|      test|   columns*>")]
	[Arguments("columns(this is a test of columns,10,3,R,0,0,<*,|,*>,1)", "<*      this|        is|         a*>\n<*      test|        of|   columns*>")]
	[Arguments("columns(this@is@a@test@of@columns,10,3,R,0,0,<*,|,*>,1,@)", "<*      this|        is|         a*>\n<*      test|        of|   columns*>")]
	[Arguments("columns(this@is@a@test@of@columns,10,3,R,0,0,<*,|,*>,1,@,-=*=-)", "<*-=*=--this|-=*=--=*is|-=*=--=*=a*>\n<*-=*=--test|-=*=--=*of|-=*columns*>")]
	public async Task Columns(string code, string expected)
	{
		await Assert.That(await RhostEval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task LrandGivesCountNumbersInRange()
	{
		await Assert.That(await RhostEval("[words(setr(0,lrand(3,20,5)))] [gte(lmin(%q0),3)] [lte(lmax(%q0),20)]"))
			.IsEqualTo("5 1 1");
		await Assert.That(await RhostEval("[words(setr(0,lrand(1,6,3)))] [gte(lmin(%q0),1)] [lte(lmax(%q0),6)]"))
			.IsEqualTo("3 1 1");
	}

	[Test]
	public async Task EscapeXOfAnAttribute()
	{
		var thing = await CreateThingAsync("RhostHelpEscapeX");
		await Cmd($"@va {thing}=%crred%cn [add(1,1)]");

		await Assert.That(await RhostEvalAs(thing, "escapex(v(va),a)")).IsEqualTo("%crred%cn \\[add\\(1\\,1\\)\\]");
		await Assert.That(await RhostEvalAs(thing, "escapex(v(va),ra)")).IsEqualTo("\\%crred\\%cn [add(1,1)]");
	}

	[Test]
	public async Task U2DefaultAndU2LDefault()
	{
		var thing = await CreateThingAsync("RhostHelpU2Default");
		await Cmd($"&TEST {thing}=[center(%0,5,*)]");

		await Assert.That(await RhostEvalAs(thing, "u2default(Test,-- BOOM --,ACK)")).IsEqualTo("*ACK*");
		await Assert.That(await RhostEvalAs(thing, "u2ldefault(Test,-- BOOM --,ACK)")).IsEqualTo("*ACK*");

		await Cmd($"&TEST {thing}");

		await Assert.That(await RhostEvalAs(thing, "u2default(me/Test,-- BOOM --,ACK)")).IsEqualTo("-- BOOM --");
		await Assert.That(await RhostEvalAs(thing, "u2ldefault(me/Test,-- BOOM --,ACK)")).IsEqualTo("-- BOOM --");
	}

	[Test]
	public async Task U2LocalKeepsTheCallersRegisters()
	{
		var thing = await CreateThingAsync("RhostHelpU2Local");
		await Cmd($"&FRUIT {thing}=apples bananas oranges pears");
		await Cmd($"&SUB-FUNCTION {thing}=[setq(0,v(FRUIT))][extract(%q0,match(%q0,%0),1)]");
		await Cmd($"&TOP-FUNCTION {thing}=[setq(0,are delicious!)][u2local(SUB-FUNCTION,%0)] %q0");

		await Assert.That(await RhostEvalAs(thing, "u2(TOP-FUNCTION,b*)")).IsEqualTo("bananas are delicious!");

		await Cmd($"&TOP-FUNCTION {thing}=[setq(0,are delicious!)][u2(SUB-FUNCTION,%0)] %q0");

		await Assert.That(await RhostEvalAs(thing, "u2(TOP-FUNCTION,b*)")).IsEqualTo("bananas apples bananas oranges pears");
	}

	[Test]
	[Arguments("[setq(0,external)][r(0)][u(va)][r(0)]", "external(before R0: external) --My R0:internal--internal")]
	[Arguments("[setq(0,external)][r(0)][localize(u(va))][r(0)]", "external(before R0: external) --My R0:internal--external")]
	[Arguments("[privatize(1)][setq(0,external)][r(0)][localize(u(va))][r(0)]", "external(before R0: ) --My R0:internal--external")]
	public async Task PrivatizeHidesRegistersFromCalls(string code, string expected)
	{
		var thing = await CreateThingAsync("RhostHelpPrivatize");
		await Cmd($"@va {thing}=(before R0: [r(0)])[setq(0,internal)] --My R0:[r(0)]--");

		await Assert.That(await RhostEvalAs(thing, code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[setr(0,ONE)]:[r(1)]", "[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(va))], %q0:%q1", "TWO:CHEESE, ONE:, TWO:CHEESE")]
	[Arguments("[setr(0,ONE)]:[r(1)]", "[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(va),0)], %q0:%q1", "TWO:CHEESE, ONE:CHEESE, TWO:CHEESE")]
	[Arguments("[setr(0,ONE)]:[r(1)]", "[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(va),1)], %q0:%q1", "TWO:CHEESE, ONE:, ONE:CHEESE")]
	[Arguments("[setr(0,ONE)]:[r(1)]", "[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(va),0,1)], %q0:%q1", "TWO:CHEESE, ONE:, ONE:CHEESE")]
	[Arguments("[setr(0,O)]:[r(1)]", "[setr(0,T)]:[setr(1,C)], [privatize(1,u(va),0 3 4,,1)], %q0:%q1", "T:C, O:C, T:C")]
	[Arguments("[setr(0,O)]:[r(1)]", "[setr(0,T)]:[setr(1,C)], [privatize(1,u(va),0 1 3 4,,1)], %q0:%q1", "T:C, O:, T:C")]
	public async Task PrivatizeWithRegisterLists(string attribute, string code, string expected)
	{
		var thing = await CreateThingAsync("RhostHelpPrivatizeList");
		await Cmd($"@va {thing}={attribute}");

		await Assert.That(await RhostEvalAs(thing, code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[while(EVAL_FN,COND_FN,foo bar meep flibble baz,4)]", "oof rab peem")]
	[Arguments("[while(EVAL_FN,COND_FN,foo|bar|meep|flibble|baz,4,|,-)]", "oof-rab-peem")]
	public async Task While(string code, string expected)
	{
		var thing = await CreateThingAsync("RhostHelpWhile");
		await Cmd($"&EVAL_FN {thing}=[reverse(%0)]");
		await Cmd($"&COND_FN {thing}=[strlen(%0)]");

		await Assert.That(await RhostEvalAs(thing, code)).IsEqualTo(expected);
	}
}
