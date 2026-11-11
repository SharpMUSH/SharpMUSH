using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// RhostMUSH function calls the import writes as SharpMUSH's counterpart. Each rewritten example from
/// RhostMUSH's help gives RhostMUSH's answer here.
/// </summary>
public class RhostFunctionCallRewriterTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("-[ljc(foo,6)]-", "-foo   -")]
	[Arguments("-[ljc(this is foo,6)]-", "-this i-")]
	[Arguments("-[ljc(foo,6,.)]-", "-foo...-")]
	[Arguments("-[rjc(foo,6)]-", "-   foo-")]
	[Arguments("-[rjc(this is foo,6)]-", "-this i-")]
	[Arguments("streq(this is a test,THIS is A TesT)", "1")]
	[Arguments("streq(this is a test,this is a test?)", "0")]
	[Arguments("[ncomp(2,1)] [ncomp(1,2)] [ncomp(2,2)] [ncomp(1.1,1.2)]", "1 -1 0 -1")]
	[Arguments("[power10(2)] [power10(5)]", "100 100000")]
	[Arguments("[xnor(1,0)] [xnor(0,0)] [xnor(0,1)] [xnor(1,1)]", "0 1 0 1")]
	[Arguments("nummatch(This is a test,test)", "1")]
	[Arguments("nummatch(This test is a test of nummatch. A test is fun,test)", "3")]
	[Arguments("nummatch(This test is silly,fred)", "0")]
	[Arguments("nummatch(a@test@b@TEST,test,@)", "2")]
	[Arguments("sort(randmatch(this test this this test,*this*))", "1 3 4")]
	[Arguments("sort(randmatch(this@test@this@this@test,*this*,@))", "1 3 4")]
	[Arguments("-[chomp(%rthis is a test%r)]-", "-this is a test-")]
	[Arguments("-[chomp(%rthis is a test%r,r)]-", "-\nthis is a test-")]
	[Arguments("pedit(This is a test,is,x)", "Thx x a test")]
	[Arguments("pedit(Atlantic,^,Trans)", "TransAtlantic")]
	[Arguments("pedit(Atlantic,$,!)", "Atlantic!")]
	[Arguments("pedit(This is a test,is,was,was,were)", "Thwere were a test")]
	[Arguments("pedit(abc,a,x,b)", "xbc")]
	[Arguments("medit(This is a test,is,x,test,moo,moo,bread)", "Thx x a bread")]
	[Arguments("medit(abc,a,b,b,c)", "ccc")]
	[Arguments("eq(startsecs(),convtime(starttime()))", "1")]
	[Arguments("[isint(rebootsecs())] [gte(rebootsecs(),startsecs())]", "1 1")]
	[Arguments("lnum2(5)", "0 1 2 3 4")]
	[Arguments("lnum2(10,5,+)", "5 6 7 8 9 10 11 12 13 14")]
	[Arguments("lnum2(10,5,+,2)", "5 7 9 11 13 15 17 19 21 23")]
	[Arguments("lnum2(10,20,-)", "20 19 18 17 16 15 14 13 12 11")]
	[Arguments("lnum2(3,,-)", "0")]
	[Arguments("lnum2(5,3,-,2)", "3 1")]
	[Arguments("lnum2(3,9,-,3)", "9 6 3")]
	[Arguments("lnum2(5,3,,2)", "3 5 7")]
	[Arguments("lnum2(6,3,,2)", "3 5 7")]
	[Arguments("[xnor(1,1,1)] [xnor(1,0,0)] [xnor(1,1,0)]", "1 1 0")]
	[Arguments("streq(ljc(abcdef,3),ABC)", "1")]
	[Arguments("[andflag(#1,wizard,!going)] [andflag(#1,wizard,going)]", "1 0")]
	[Arguments("[orflag(#1,going,wizard)] [orflag(#1,going,halt)]", "1 0")]
	[Arguments("[strmatch(moneyname(),config(money_singular))] [strmatch(moneyname(1),config(money_singular))]", "1 1")]
	[Arguments("[strmatch(moneyname(0),config(money_plural))] [strmatch(moneyname(50),config(money_plural))] [strmatch(moneyname(-5),config(money_plural))]", "1 1 1")]
	[Arguments("[strmatch(moneyname(add(25,25)),config(money_plural))] [strmatch(moneyname([sub(2,1)]),config(money_singular))]", "1 1")]
	[Arguments("[setq(moneyname,kept)][strmatch(moneyname(add(1,1)),config(money_plural))] %q<moneyname>", "1 kept")]
	[Arguments("[gte(bittype(#1),7)] [gt(bittype(#1),6)] [lt(bittype(#1),5)] [lte(bittype(#1),4)]", "1 1 0 0")]
	[Arguments("[gte(5,bittype(#1))] [lt(3,[bittype(#1)])] [gte(bittype(),5)] [gte(bittype(#1),0)] [gt(bittype(#1),7)]", "0 1 1 1 0")]
	public async Task RewrittenCallsGiveRhostMushAnswers(string rhost, string expected)
	{
		var rewritten = RhostSoftcodeRewriter.Rewrite(rhost);

		await Assert.That(rewritten.Changed).IsTrue();
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain(rewritten.Text))).ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task OneArgumentTagIsATagLookup()
	{
		var result = RhostSoftcodeRewriter.Rewrite("@trigger [tag(weather)]/update; think [tag(b,i)]");

		await Assert.That(result.Text).IsEqualTo("@trigger [tagref(weather)]/update; think [tag(b,i)]");
		await Assert.That(result.Notes).IsEquivalentTo([
			"tag() written as tagref()",
			"tag() with 2 argument(s) left as written: SharpMUSH's tag() is another function"
		]);
	}

	[Test]
	public async Task NestedCallsAreRewrittenInsideOut()
	{
		var result = RhostSoftcodeRewriter.Rewrite("think [streq(ljc(%0,3),abc)]");

		await Assert.That(result.Text).IsEqualTo("think [not(comp(ljust(left(%0,3),3),abc,I))]");
		await Assert.That(result.Notes).IsEquivalentTo(["ljc() written as ljust()", "streq() written as not()"]);
	}

	[Test]
	[Arguments("think mystreq(a,b)")]
	[Arguments("think streq")]
	[Arguments("think \\streq(a,b)")]
	[Arguments("think streq(a,b")]
	public async Task LeavesWhatIsNotACall(string body)
	{
		var result = RhostSoftcodeRewriter.Rewrite(body);

		await Assert.That(result.Text).IsEqualTo(body);
		await Assert.That(result.Notes).IsEmpty();
	}

	[Test]
	public async Task LeavesAShapeNoRuleCoversAndSaysSo()
	{
		var result = RhostSoftcodeRewriter.Rewrite("think [lnum2(%0,5,+)] [xnor(1)]");

		await Assert.That(result.Text).IsEqualTo("think [lnum2(%0,5,+)] [xnor(1)]");
		await Assert.That(result.Notes).IsEquivalentTo([
			"lnum2() with 3 argument(s) left as written: SharpMUSH has no lnum2()",
			"xnor() with 1 argument(s) left as written: SharpMUSH has no xnor()"
		]);
	}

	[Test]
	public async Task MultiPairEditsAreEditsInOrder()
	{
		var result = RhostSoftcodeRewriter.Rewrite("think [pedit(%0,a,b,c,d)] [medit(%0,a,b,c)]");

		await Assert.That(result.Text).IsEqualTo("think [edit(edit(%0,a,b),c,d)] [medit(%0,a,b,c)]");
		await Assert.That(result.Notes).IsEquivalentTo([
			"pedit() written as edit()",
			"medit() with 4 argument(s) left as written: SharpMUSH has no medit()"
		]);
	}

	[Test]
	[Arguments("think [ljc(abcdef,setr(0,add(%q0,1)))]", "ljc() with a call in argument 2 left as written: its rewrite would evaluate that argument more than once")]
	[Arguments("think [rjc(abc,[v(width)],.)]", "rjc() with a call in argument 2 left as written: its rewrite would evaluate that argument more than once")]
	public async Task LeavesACallWhoseRewriteWouldEvaluateAnArgumentTwice(string body, string note)
	{
		var result = RhostSoftcodeRewriter.Rewrite(body);

		await Assert.That(result.Text).IsEqualTo(body);
		await Assert.That(result.Notes).IsEquivalentTo([note]);
	}

	[Test]
	public async Task LeavesWhatLitHoldsAsWritten()
	{
		var result = RhostSoftcodeRewriter.Rewrite("think [lit(streq(a,b))] [streq(a,b)]");

		await Assert.That(result.Text).IsEqualTo("think [lit(streq(a,b))] [not(comp(a,b,I))]");
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain("[lit(streq(a,b))]"))).ToPlainText()).IsEqualTo("streq(a,b)");
	}

	[Test]
	public async Task ABitTypeComparisonAsksForARole()
	{
		var result = RhostSoftcodeRewriter.Rewrite("think [gte(bittype(%#),5)] [lt(2,bittype(*Bob))] [gte(bittype(%#),%0)] [gte(1,2)]");

		await Assert.That(result.Text).IsEqualTo(
			"think [hasrole(owner(%#),wizard,1)] [hasrole(owner(*Bob),builder,1)] [gte(bittype(%#),%0)] [gte(1,2)]");
		await Assert.That(result.Notes).IsEquivalentTo([
			"gte() comparing bittype() written with hasrole()",
			"lt() comparing bittype() written with hasrole()"
		]);
	}

	/// <summary>A totem is a role here, so the totem tests are hasrole() and ltotems() is roles() of the Totems category.</summary>
	[Test]
	[Arguments("think hastotem(%#,Shiny)", "think hasrole(%#,totem-shiny)")]
	[Arguments("think hastotem(%#,%0)", "think hasrole(%#,totem-%0)")]
	[Arguments("think hastotem(%#,Wizard)", "think hasrole(%#,totem-wizard)")]
	[Arguments("think andtotem(%#,shiny !dull)", "think and(hasrole(%#,totem-shiny),not(hasrole(%#,totem-dull)))")]
	[Arguments("think ortotem(%#,shiny dull)", "think or(hasrole(%#,totem-shiny),hasrole(%#,totem-dull))")]
	[Arguments("think andtotem(%#,!dull)", "think not(hasrole(%#,totem-dull))")]
	[Arguments("think ltotems(%#)", "think iter(roles(%#,Totems),after(##,totem-))")]
	public async Task TotemTestsBecomeRoleTests(string rhost, string expected)
	{
		var result = RhostSoftcodeRewriter.Rewrite(rhost);

		await Assert.That(result.Text).IsEqualTo(expected);
	}

	[Test]
	public async Task TotemFunctionsWithoutARoleFormAreNoted()
	{
		var result = RhostSoftcodeRewriter.Rewrite("think [totems(%#)] [andtotem(%#,%0)]");

		await Assert.That(result.Text).IsEqualTo("think [totems(%#)] [andtotem(%#,%0)]");
		await Assert.That(result.Notes).Contains("totems() with 1 argument(s) left as written: totems are roles here; use hasrole(), roles() and @role");
	}

	/// <summary>@totem gives and takes roles; a form the import cannot read is left for a person to check.</summary>
	[Test]
	[Arguments("@totem me=shiny", "@role/assign me=totem-shiny")]
	[Arguments("@totem %#=Shiny !Dull", "@role/assign %#=totem-shiny;@role/unassign %#=totem-dull")]
	[Arguments("$+shine:@totem %#=shiny !dull;@pemit %#=Done.", "$+shine:@role/assign %#=totem-shiny;@role/unassign %#=totem-dull;@pemit %#=Done.")]
	[Arguments("@switch %0=1,@totem %#=shiny !dull,@totem %#=dull", "@switch %0=1,{@role/assign %#=totem-shiny;@role/unassign %#=totem-dull},@role/assign %#=totem-dull")]
	[Arguments("@force me=@totem %#=a b", "@force me={@role/assign %#=totem-a;@role/assign %#=totem-b}")]
	[Arguments("think [force(me,@totem me=shiny)]", "think [force(me,@role/assign me=totem-shiny)]")]
	[Arguments("@totem me=shiny\r\n@pemit me=x", "@role/assign me=totem-shiny\r\n@pemit me=x")]
	[Arguments("@totem/clear me", "@totem/clear me")]
	[Arguments("@totem me=[v(list)]", "@totem me=[v(list)]")]
	[Arguments("think @totem me=shiny", "think @totem me=shiny")]
	public async Task TotemCommandsBecomeRoleCommands(string rhost, string expected)
	{
		var result = RhostSoftcodeRewriter.Rewrite(rhost);

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(result.Notes).Count().IsEqualTo(rhost.StartsWith("think @") ? 0 : 1);
	}
}
