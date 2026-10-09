using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>setqm()</c> and <c>setrm()</c>: RhostMUSH's in-order setq()/setr(), where each value sees the
/// registers set to its left. setq() itself evaluates every argument first, as PennMUSH does.
/// </summary>
public class SetQMFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code) =>
		(await Parser.FunctionParse(MarkupText.Plain(code)))?.Message?.ToPlainText() ?? string.Empty;

	[Test]
	public async Task SetQM_LaterValueSeesEarlierRegister()
	{
		var result = await Eval("[setqm(sqm1_count,3,sqm1_total,mul(%q<sqm1_count>,2))]%q<sqm1_count>/%q<sqm1_total>");

		await Assert.That(result).IsEqualTo("3/6");
	}

	[Test]
	public async Task SetQ_EvaluatesEveryValueBeforeSettingAny()
	{
		var result = await Eval("[setq(sqm2_count,3,sqm2_total,x%q<sqm2_count>x)]%q<sqm2_count>/%q<sqm2_total>");

		await Assert.That(result).IsEqualTo("3/xx");
	}

	[Test]
	public async Task SetQM_LaterNameSeesEarlierRegister()
	{
		var result = await Eval("[setqm(sqm3_section,cmd,sqm3_%q<sqm3_section>_switches,list)]%q<sqm3_cmd_switches>");

		await Assert.That(result).IsEqualTo("list");
	}

	[Test]
	public async Task SetQM_OverwritesInOrder()
	{
		var result = await Eval("[setqm(sqm4_text,a,sqm4_text,%q<sqm4_text>b,sqm4_text,%q<sqm4_text>c)]%q<sqm4_text>");

		await Assert.That(result).IsEqualTo("abc");
	}

	[Test]
	public async Task SetRM_ReturnsEveryValueSpaceSeparated()
	{
		var result = await Eval("setrm(sqm5_first,foo,sqm5_second,%q<sqm5_first>bar)");

		await Assert.That(result).IsEqualTo("foo foobar");
	}

	[Test]
	public async Task SetRM_TrailingDelimiter()
	{
		var result = await Eval("setrm(sqm6_a,1,sqm6_b,add(%q<sqm6_a>,1),sqm6_c,add(%q<sqm6_b>,1),|)");

		await Assert.That(result).IsEqualTo("1|2|3");
	}

	[Test]
	public async Task SetRM_EmptyDelimiter()
	{
		var result = await Eval("setrm(sqm7_a,foo,sqm7_b,bar,null())");

		await Assert.That(result).IsEqualTo("foobar");
	}

	[Test]
	public async Task SetRM_ThreeArgumentsRefused()
	{
		var result = await Eval("setrm(sqm8_a,foo,|)");

		await Assert.That(result).IsEqualTo("#-1 FUNCTION (SETRM) EXPECTS AN EVEN NUMBER OF ARGUMENTS");
	}

	[Test]
	public async Task SetQM_OddArgumentsRefused()
	{
		var result = await Eval("setqm(sqm9_a,foo,sqm9_b)");

		await Assert.That(result).IsEqualTo("#-1 FUNCTION (SETQM) EXPECTS AN EVEN NUMBER OF ARGUMENTS");
	}

	[Test]
	public async Task SetQM_BadNameStillSetsTheRest()
	{
		var result = await Eval("[setqm(sqm10 bad,foo,sqm10_good,bar)]/%q<sqm10_good>");

		await Assert.That(result).IsEqualTo("#-1 REGISTER NAME INVALID/bar");
	}

	[Test]
	public async Task SetRM_BadNameReturnsError()
	{
		var result = await Eval("setrm(sqm11_good,foo,sqm11 bad,bar)");

		await Assert.That(result).IsEqualTo("#-1 REGISTER NAME INVALID");
	}

	/// <summary>The examples in <c>help setqm()</c>, as written there.</summary>
	[Test]
	[Arguments("[setqm(cmd_count, 3, cmd_total, mul(%q<cmd_count>, 2))]%q<cmd_total>", "6")]
	[Arguments("[setq(cmd_count, 3, cmd_total, mul(%q<cmd_count>, 2))]%q<cmd_total>", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments("[setqm(cmd_section, list, cmd_%q<cmd_section>_switches, all unread)]%q<cmd_list_switches>", "all unread")]
	[Arguments("setrm(page_first, 1, page_last, add(%q<page_first>, 19))", "1 20")]
	[Arguments("setrm(page_first, 1, page_last, add(%q<page_first>, 19), -)", "1-20")]
	[Arguments("setrm(word_one, foo, word_two, %q<word_one>bar, )", "foofoobar")]
	public async Task HelpExamples(string code, string expected)
	{
		var result = await Eval(code);

		await Assert.That(result).IsEqualTo(expected);
	}
}
