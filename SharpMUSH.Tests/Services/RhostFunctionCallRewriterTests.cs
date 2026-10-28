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
	[Arguments("lnum2(5)", "0 1 2 3 4")]
	[Arguments("streq(ljc(abcdef,3),ABC)", "1")]
	[Arguments("[andflag(#1,wizard,!going)] [andflag(#1,wizard,going)]", "1 0")]
	[Arguments("[orflag(#1,going,wizard)] [orflag(#1,going,halt)]", "1 0")]
	[Arguments("[strmatch(moneyname(),config(money_singular))] [strmatch(moneyname(1),config(money_singular))]", "1 1")]
	[Arguments("[strmatch(moneyname(0),config(money_plural))] [strmatch(moneyname(50),config(money_plural))] [strmatch(moneyname(-5),config(money_plural))]", "1 1 1")]
	public async Task RewrittenCallsGiveRhostMushAnswers(string rhost, string expected)
	{
		var rewritten = RhostSoftcodeRewriter.Rewrite(rhost);

		await Assert.That(rewritten.Changed).IsTrue();
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain(rewritten.Text))).ToPlainText()).IsEqualTo(expected);
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
		var result = RhostSoftcodeRewriter.Rewrite("think [lnum2(10,5,+)] [xnor(1,1,1)]");

		await Assert.That(result.Text).IsEqualTo("think [lnum2(10,5,+)] [xnor(1,1,1)]");
		await Assert.That(result.Notes).IsEquivalentTo([
			"lnum2() with 3 argument(s) left as written: SharpMUSH has no lnum2()",
			"xnor() with 3 argument(s) left as written: SharpMUSH has no xnor()"
		]);
	}
}
