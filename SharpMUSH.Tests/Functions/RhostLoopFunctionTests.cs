using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's loops beside iter(): <c>nsiter()</c>, <c>citer()</c>, <c>inf()</c> and <c>while()</c>.
/// The expected results are what an unmodified RhostMUSH server answers for the same input.
/// </summary>
public class RhostLoopFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string code) =>
		(await Parser.FunctionParse(MarkupText.Plain(code)))?.Message?.ToPlainText() ?? string.Empty;

	[Test]
	[Arguments("nsiter(a b c,##-#@)", "a-1b-2c-3")]
	[Arguments("nsiter(a|b|c,##,|)", "abc")]
	[Arguments("nsiter(This is a test,strlen(##))", "4214")]
	[Arguments("nsiter(  a   b  ,<##>)", "<a><b>")]
	[Arguments("nsiter(Was it a cat I saw,words(##),s)", "141")]
	[Arguments("nsiter(lnum(4),--+)", "--+--+--+--+")]
	[Arguments("[nsiter(,x)]|", "|")]
	public async Task NsIter(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("citer(boo!,##)", "b o o !")]
	[Arguments("citer(boo!,##-#@)", "b-1 o-2 o-3 !-4")]
	[Arguments("citer(testing,strlen(##))", "1 1 1 1 1 1 1")]
	[Arguments("citer(bob,## WHEE ##,@)", "b WHEE b@o WHEE o@b WHEE b")]
	[Arguments("citer(a b,<##>)", "<a> < > <b>")]
	[Arguments("citer(bob,##,)", "b o b")]
	[Arguments("[citer(,x)]|", "|")]
	public async Task CIter(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("inf(a b c,##-#@)", "a-1 b-2 c-3")]
	[Arguments("inf(a b c,%i0,,-)", "a-b-c")]
	[Arguments("inf(:5:>:,[if(gt(##,8),ibreak())]##)", "5 6 7 8 9")]
	[Arguments("inf(:5:<:,[if(lt(##,2),ibreak())]##)", "5 4 3 2 1")]
	[Arguments("inf(:-3:>:,[if(gt(##,0),ibreak())]#@:##)", "1:-3 2:-2 3:-1 4:0 5:1")]
	[Arguments("inf(:5:>:,[if(gt(##,7),ibreak())]##,,-)", "5-6-7-8")]
	[Arguments("inf(:3:>:,##[if(eq(#@,3),ibreak())],,)", "345")]
	[Arguments("inf(:3:>:,[if(eq(#@,3),ibreak())]%i0-[inum(0)])", "3-1 4-2 5-3")]
	[Arguments("inf(:2.5:>:,[if(eq(#@,3),ibreak())]##)", "2 3 4")]
	[Arguments("inf(:0:<:,[if(eq(#@,3),ibreak())]##)", "0 -1 -2")]
	[Arguments("inf(:a:>:,[if(eq(#@,3),ibreak())]##)", "0 1 2")]
	[Arguments("inf(:5:x:,##)", ":5:x:")]
	[Arguments("inf(:5:>,##)", ":5:>")]
	[Arguments("inf(:5:>: a,##)", ":5:>: a")]
	[Arguments("[inf(,x)]|", "|")]
	public async Task Inf(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>
	/// An endless inf() with no ibreak() stops by itself: RhostMUSH stops at its buffer size, and
	/// SharpMUSH at the function invocation limit, a count it never passes.
	/// </summary>
	[Test]
	public async Task Inf_WithoutBreakStopsAtTheInvocationLimit()
	{
		var limit = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>()
			.CurrentValue.Limit.FunctionInvocationLimit;

		var words = await Eval("words(inf(:1:>:,x))");

		await Assert.That(int.Parse(words)).IsEqualTo((int)limit);
	}

	private async Task<int> CreateWhileObjectAsync()
	{
		var created = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName("WhileObj"));
		var number = DBRef.Parse(created.Message.ToPlainText()).Number;
		foreach (var (attribute, value) in new[]
						{
							("EVAL_FN", "[reverse(%0)]"),
							("COND_FN", "[strlen(%0)]"),
							("CNT", "[setq(wcnt,add(%q<wcnt>,1))]%0"),
							("POS", "%0:%1")
						})
		{
			await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} #{number}={value}"));
		}

		return number;
	}

	[Test]
	[Arguments("while(#N/EVAL_FN,#N/COND_FN,foo bar meep flibble baz,4)", "oof rab peem")]
	[Arguments("while(#N/EVAL_FN,#N/COND_FN,foo|bar|meep|flibble|baz,4,|,-)", "oof-rab-peem")]
	[Arguments("while(#N/EVAL_FN,#N/COND_FN,foo|bar|meep|flibble|baz,4,|)", "oof|rab|peem")]
	[Arguments("while(#N/EVAL_FN,#N/COND_FN,foo bar,9)", "oof rab")]
	[Arguments("while(#N/EVAL_FN,#N/COND_FN,foo bar,3)", "oof")]
	[Arguments("while(#N/EVAL_FN,#N/COND_FN,ab abc,3,,)", "ba cba")]
	[Arguments("[while(#N/EVAL_FN,#N/COND_FN,,9)]|", "|")]
	[Arguments("[while(#N/EVAL_FN,#N/NOPE,foo bar,4)]|", "|")]
	[Arguments("[while(#N/NOPE,#N/COND_FN,foo bar,3)]|", "|")]
	[Arguments("while(#N/POS,#N/POS,a b c,x)", "a: b: c:")]
	[Arguments("[setq(wcnt,0)][while(#N/CNT,#N/CNT,a b c,b)]-%q<wcnt>", "a b-2")]
	[Arguments("[setq(wcnt,0)][while(#N/CNT,#N/EVAL_FN,a b c,b)]-%q<wcnt>", "a b-2")]
	public async Task While(string code, string expected)
	{
		var number = await CreateWhileObjectAsync();

		await Assert.That(await Eval(code.Replace("#N", $"#{number}"))).IsEqualTo(expected);
	}

	/// <summary>When the two attributes hold different code, both run for each item.</summary>
	[Test]
	public async Task While_DifferentCodeRunsBoth()
	{
		var number = await CreateWhileObjectAsync();
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&CNT2 #{number}=[setq(wcnt,add(1,%q<wcnt>))]%0"));

		var result = await Eval($"[setq(wcnt,0)][while(#{number}/CNT,#{number}/CNT2,a b c,b)]-%q<wcnt>");

		await Assert.That(result).IsEqualTo("a b-4");
	}

	/// <summary>The examples in the help for nsiter(), citer() and inf(), as written there.</summary>
	[Test]
	[Arguments("nsiter(a b c, ##-#@)", "a-1b-2c-3")]
	[Arguments("nsiter(This is a test, strlen(##))", "4214")]
	[Arguments("nsiter(lnum(4), --+)", "--+--+--+--+")]
	[Arguments("citer(boo!, ##-#@)", "b-1 o-2 o-3 !-4")]
	[Arguments("citer(a b, <##>)", "<a> < > <b>")]
	[Arguments("citer(bob, ## WHEE ##, @)", "b WHEE b@o WHEE o@b WHEE b")]
	[Arguments("inf(:5:>:, [if(gt(##, 8), ibreak())]##)", "5 6 7 8 9")]
	[Arguments("inf(:5:<:, [if(lt(##, 2), ibreak())]##)", "5 4 3 2 1")]
	[Arguments("inf(:3:>:, [if(eq(#@, 3), ibreak())]##, , -)", "3-4-5")]
	public async Task HelpExamples(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>Either side may be a #lambda, as with map(); this one is SharpMUSH's own.</summary>
	[Test]
	public async Task While_Lambda()
	{
		var result = await Eval(@"while(#lambda/ucstr\(\%0\),#lambda/\%0,a b c d,c)");

		await Assert.That(result).IsEqualTo("A B C");
	}
}
