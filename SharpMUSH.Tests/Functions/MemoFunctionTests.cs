using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// memo() answers as u() does, gives a kept answer again while what it read is unchanged, and refuses code with side
/// effects. Each test works on objects of its own: the memo store is shared by the session.
/// </summary>
public class MemoFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async Task Command(string command)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> Evaluate(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))!.Message.ToPlainText();

	/// <summary>A new thing, by dbref.</summary>
	private async Task<string> Thing(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		await Command($"@create {name}");
		return await Evaluate($"[locate(me,{name},*)]");
	}

	[Test]
	public async Task AnswersAsUDoes()
	{
		var thing = await Thing("MemoDouble");
		await Command($"&DOUBLE {thing}=mul(%0,2)");

		await Assert.That(await Evaluate($"[memo({thing}/DOUBLE,21)]")).IsEqualTo("42");
		await Assert.That(await Evaluate($"[memo({thing}/DOUBLE,21)]")).IsEqualTo("42");
		await Assert.That(await Evaluate($"[memo({thing}/DOUBLE,5)]")).IsEqualTo("10");
	}

	[Test]
	public async Task AKeptAnswerIsGivenAgainWithoutRunningTheCode()
	{
		var thing = await Thing("MemoName");
		await Command($"&WHO {thing}=name(%!)");
		var first = await Evaluate($"[memo({thing}/WHO)]");

		var renamed = TestIsolationHelpers.GenerateUniqueName("MemoRenamed");
		await Command($"@name {thing}={renamed}");

		await Assert.That(await Evaluate($"[memo({thing}/WHO)]")).IsEqualTo(first)
			.Because("a name is not an attribute read, so the kept answer stands until the window passes");
		await Assert.That(await Evaluate($"[u({thing}/WHO)]")).IsEqualTo(renamed);
	}

	[Test]
	public async Task AChangedAttributeItReadDropsTheAnswer()
	{
		var thing = await Thing("MemoBonus");
		await Command($"&BONUS {thing}=10");
		await Command($"&TOTAL {thing}=add(%0,v(BONUS))");
		await Assert.That(await Evaluate($"[memo({thing}/TOTAL,5)]")).IsEqualTo("15");

		await Command($"&BONUS {thing}=20");

		await Assert.That(await Evaluate($"[memo({thing}/TOTAL,5)]")).IsEqualTo("25");
	}

	[Test]
	public async Task AnAttributeThatAppearsDropsTheAnswer()
	{
		var thing = await Thing("MemoAppear");
		await Command($"&READ {thing}=[default(me/EXTRA,none)]");
		await Assert.That(await Evaluate($"[memo({thing}/READ)]")).IsEqualTo("none");

		await Command($"&EXTRA {thing}=some");

		await Assert.That(await Evaluate($"[memo({thing}/READ)]")).IsEqualTo("some");
	}

	[Test]
	public async Task ChangingTheCodeDropsTheAnswer()
	{
		var thing = await Thing("MemoCode");
		await Command($"&FN {thing}=first");
		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("first");

		await Command($"&FN {thing}=second");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("second");
	}

	[Test]
	public async Task AnEnclosingMemoDependsOnWhatAnInnerKeptAnswerRead()
	{
		var thing = await Thing("MemoNested");
		await Command($"&DATA {thing}=stone");
		await Command($"&INNER {thing}=ucstr(v(DATA))");
		await Command($"&OUTER {thing}=<[memo(me/INNER)]>");
		// The inner answer is kept first, so the outer one is built from a kept answer, not from a read of DATA.
		await Assert.That(await Evaluate($"[memo({thing}/INNER)]")).IsEqualTo("STONE");
		await Assert.That(await Evaluate($"[memo({thing}/OUTER)]")).IsEqualTo("<STONE>");

		await Command($"&DATA {thing}=water");

		await Assert.That(await Evaluate($"[memo({thing}/OUTER)]")).IsEqualTo("<WATER>");
	}

	[Test]
	public async Task ASideEffectIsRefused()
	{
		var thing = await Thing("MemoSet");
		await Command($"&MARK {thing}=[attrib_set(%!/SEEN,1)]done");

		await Assert.That(await Evaluate($"[memo({thing}/MARK)]")).IsEqualTo("#-1 MEMO REFUSES SIDE EFFECTS");
		await Assert.That(await Evaluate($"[hasattr({thing},SEEN)]")).IsEqualTo("0");
	}

	[Test]
	public async Task ASideEffectReachedThroughUIsRefused()
	{
		var thing = await Thing("MemoDeep");
		await Command($"&WRITE {thing}=[attrib_set(%!/SEEN,1)]");
		await Command($"&FN {thing}=[u(me/WRITE)]done");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES SIDE EFFECTS");
		await Assert.That(await Evaluate($"[hasattr({thing},SEEN)]")).IsEqualTo("0");
	}

	[Test]
	public async Task ARefusedAnswerIsNotKept()
	{
		var thing = await Thing("MemoRefusedOnce");
		await Command($"&FN {thing}=[attrib_set(%!/SEEN,1)]done");
		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES SIDE EFFECTS");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES SIDE EFFECTS");
		await Assert.That(await Evaluate($"[hasattr({thing},SEEN)]")).IsEqualTo("0");
	}

	[Test]
	public async Task RecordingARollIsRefused()
	{
		var thing = await Thing("MemoRoll");
		await Command($"&FN {thing}=[roll(1d6,,record)]");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES NONDETERMINISTIC FUNCTIONS");
		using (SideEffectFreeEvaluation.Enter())
		{
			await Assert.That(await Evaluate("[roll(1d6,,record)]")).IsEqualTo("#-1 FUNCTION DISABLED")
				.Because("recording writes the roll, even where varying answers are allowed");
		}
	}

	[Test]
	[Arguments("[rand(100)]")]
	[Arguments("[shuffle(a b c)]")]
	[Arguments("[secs()]")]
	[Arguments("[timefmt($Y)]")]
	[Arguments("[timecalc(now)]")]
	[Arguments("[idle(me)]")]
	[Arguments("[conn(me)]")]
	[Arguments("[pid()]")]
	[Arguments("[strlen(%c)]")]
	[Arguments("[strlen(%u)]")]
	public async Task AFunctionWhoseAnswerVariesIsRefused(string code)
	{
		var thing = await Thing("MemoVaries");
		await Command($"&FN {thing}={code}");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES NONDETERMINISTIC FUNCTIONS");
	}

	[Test]
	public async Task AFunctionWhoseAnswerVariesIsRefusedThroughU()
	{
		var thing = await Thing("MemoVariesDeep");
		await Command($"&ROLL {thing}=[rand(100)]");
		await Command($"&FN {thing}=[u(me/ROLL)]");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES NONDETERMINISTIC FUNCTIONS");
	}

	[Test]
	public async Task StoppingAnEventIsRefused()
	{
		var thing = await Thing("MemoStopEvent");
		await Command($"&FN {thing}=[stopevent()]");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES SIDE EFFECTS");
	}

	[Test]
	public async Task ANewAttributeInAListingItReadDropsTheAnswer()
	{
		var thing = await Thing("MemoListing");
		await Command($"&LIST {thing}=[words(lattr(me))]");
		await Assert.That(await Evaluate($"[memo({thing}/LIST)]")).IsEqualTo("1");

		await Command($"&MORE {thing}=1");

		await Assert.That(await Evaluate($"[memo({thing}/LIST)]")).IsEqualTo("2");
	}

	[Test]
	public async Task AChangedValueInAPatternItReadDropsTheAnswer()
	{
		var thing = await Thing("MemoGrep");
		await Command($"&DATA`ONE {thing}=red");
		await Command($"&FIND {thing}=[grep(me,DATA`*,blue)]");
		await Assert.That(await Evaluate($"[memo({thing}/FIND)]")).IsEqualTo("");

		await Command($"&DATA`ONE {thing}=blue");

		await Assert.That(await Evaluate($"[memo({thing}/FIND)]")).IsEqualTo("DATA`ONE");
	}

	[Test]
	public async Task AGivenTimeIsNotRefused()
	{
		var thing = await Thing("MemoGivenTime");
		await Command($"&FN {thing}=[timefmt($Y,0,UTC)]");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("1970");
	}

	[Test]
	public async Task ASideEffectInABranchNotTakenIsStillRefused()
	{
		var thing = await Thing("MemoBranch");
		await Command($"&FN {thing}=[if(0,attrib_set(%!/SEEN,1),fine)]");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo("#-1 MEMO REFUSES SIDE EFFECTS");
	}

	[Test]
	public async Task TheReadingFormOfASideEffectFunctionIsAllowed()
	{
		var thing = await Thing("MemoReadName");
		await Command($"&FN {thing}=[strlen(name(%!))]");

		await Assert.That(await Evaluate($"[memo({thing}/FN)]")).IsEqualTo(await Evaluate($"[strlen(name({thing}))]"));
	}

	[Test]
	public async Task TheCodeHasRegistersOfItsOwn()
	{
		var thing = await Thing("MemoRegs");
		await Command($"&FN {thing}=\\[%q0\\][setq(0,inside)]%q0");

		await Assert.That(await Evaluate($"[setq(0,outside)][memo({thing}/FN)]-%q0")).IsEqualTo("[]inside-outside");
	}

	[Test]
	public async Task TheCodeCannotReachTheCallersLoop()
	{
		var thing = await Thing("MemoLoop");
		await Command($"&FN {thing}=[ibreak()]x");

		var first = await Evaluate($"[iter(a b,memo({thing}/FN))]");

		await Assert.That(await Evaluate($"[iter(a b,memo({thing}/FN))]")).IsEqualTo(first);
		await Assert.That(first).IsEqualTo("#-1 OUT OF RANGEx #-1 OUT OF RANGEx")
			.Because("ibreak() inside memo() has no loop of the caller's to stop, so both items run");
	}

	[Test]
	public async Task ALambdaIsMemoized()
	{
		await Assert.That(await Evaluate(@"[memo(#lambda/add\(\%0\,\%1\),2,3)]")).IsEqualTo("5");
	}

	[Test]
	public async Task AMissingAttributeAnswersAsUDoes()
	{
		var thing = await Thing("MemoMissing");

		await Assert.That(await Evaluate($"[memo({thing}/NOSUCH)]")).IsEqualTo(await Evaluate($"[u({thing}/NOSUCH)]"));
	}
}
