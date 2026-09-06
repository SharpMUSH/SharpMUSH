using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@include</c> inserts the included actions into the CALLING action list, so an
/// <c>@break</c>/<c>@assert</c> inside them stops the caller as well. <c>help @include</c> teaches
/// exactly that idiom (<c>sharpcmd.md</c>):
///
/// <code>
/// &amp;CHECKS me=@assert [orflags(%#,Wr)]; @break [gt(words(lwho()),%0)]
/// &amp;CMD1 me=$cmd *: @include me/CHECKS; @pemit %#=You passed.
/// </code>
///
/// and documents <c>/nobreak</c> as the switch that suppresses it.
///
/// <para>It did neither: the break stopped at the <c>@include</c> boundary, so every guard written
/// the documented way was inert and <c>/nobreak</c> was a no-op. Where the guard is an
/// authorization check that is a permission hole — the command prints its refusal and proceeds.</para>
///
/// <para>Each case writes a marker after the <c>@include</c>, present exactly when the caller was
/// allowed to keep going.</para>
/// </summary>
[NotInParallel]
public class IncludeBreakPropagationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<string> Eval(string expression)
		=> (await Parser.CommandParse(1, ConnectionService, MModule.single($"think {expression}"))).Message?.ToPlainText()?.Trim() ?? "";

	private async ValueTask Cmd(string command)
		=> await Parser.CommandParse(1, ConnectionService, MModule.single(command));

	/// <summary>Runs <paramref name="caller"/> on a fresh object and returns what it recorded.</summary>
	private async ValueTask<(string Gate, string Reached)> RunAsync(string name, string guard, string caller)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, name);
		await Cmd($"&INC`GATE {obj}={guard}");
		await Cmd($"&RUN {obj}={caller}");
		await Cmd($"@trigger {obj}/RUN");
		return (await Eval($"get({obj}/OUT`GATE)"), await Eval($"get({obj}/OUT`REACHED)"));
	}

	[Test]
	[Arguments("@assert 0=&OUT`GATE %!=refused")]
	[Arguments("@break 1=&OUT`GATE %!=refused")]
	public async ValueTask AGuardThatFires_StopsTheIncludingList(string guard)
	{
		var (gate, reached) = await RunAsync("InclBreak", guard,
			"@include %!/INC`GATE; &OUT`REACHED %!=the caller kept going");

		await Assert.That(gate).IsEqualTo("refused").Because("the guard itself must run");
		await Assert.That(reached).IsEmpty()
			.Because("an @include'd guard that fires stops the calling action list — help @include");
	}

	[Test]
	public async ValueTask AGuardThatPasses_LetsTheIncludingListContinue()
	{
		var (gate, reached) = await RunAsync("InclNoBreak", "@assert 1=&OUT`GATE %!=refused",
			"@include %!/INC`GATE; &OUT`REACHED %!=the caller kept going");

		await Assert.That(gate).IsEmpty().Because("the assertion held, so its action must not run");
		await Assert.That(reached).IsEqualTo("the caller kept going");
	}

	/// <summary>The switch that exists to suppress the propagation has to actually suppress it.</summary>
	[Test]
	public async ValueTask NoBreak_ContainsTheBreakToTheIncludedList()
	{
		var (gate, reached) = await RunAsync("InclNoBrkSw", "@assert 0=&OUT`GATE %!=refused",
			"@include/nobreak %!/INC`GATE; &OUT`REACHED %!=the caller kept going");

		await Assert.That(gate).IsEqualTo("refused");
		await Assert.That(reached).IsEqualTo("the caller kept going")
			.Because("/nobreak prevents an included @break/@assert from breaking the including list");
	}

	/// <summary>
	/// A break contained by a NESTED <c>@include/nobreak</c> must not leak out to the outer caller
	/// through the enclosing <c>@include</c> — containment has to be complete, not one level deep.
	/// </summary>
	[Test]
	public async ValueTask ANestedNoBreak_DoesNotLeakToTheOuterCaller()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclNested");
		await Cmd($"&INC`INNER {obj}=@assert 0=&OUT`GATE %!=refused");
		await Cmd($"&INC`OUTER {obj}=@include/nobreak %!/INC`INNER; &OUT`MIDDLE %!=the middle kept going");
		await Cmd($"&RUN {obj}=@include %!/INC`OUTER; &OUT`REACHED %!=the caller kept going");
		await Cmd($"@trigger {obj}/RUN");

		await Assert.That(await Eval($"get({obj}/OUT`GATE)")).IsEqualTo("refused");
		await Assert.That(await Eval($"get({obj}/OUT`MIDDLE)")).IsEqualTo("the middle kept going")
			.Because("/nobreak contains the inner break");
		await Assert.That(await Eval($"get({obj}/OUT`REACHED)")).IsEqualTo("the caller kept going")
			.Because("a break the inner /nobreak swallowed must not resurface in the outer caller");
	}

	/// <summary>
	/// A break propagates through every plain <c>@include</c> above it, not one level:
	/// <c>do_entry</c> RETURNS whether a break happened, so each level stops and reports it upward
	/// (<c>src/cque.c:1209-1251</c>).
	/// </summary>
	[Test]
	public async ValueTask ABreak_PropagatesThroughEveryLevelOfPlainIncludes()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclThree");
		await Cmd($"&INC`INNER {obj}=@assert 0=&OUT`GATE %!=refused");
		await Cmd($"&INC`OUTER {obj}=@include %!/INC`INNER; &OUT`MIDDLE %!=the middle kept going");
		await Cmd($"&RUN {obj}=@include %!/INC`OUTER; &OUT`REACHED %!=the caller kept going");
		await Cmd($"@trigger {obj}/RUN");

		await Assert.That(await Eval($"get({obj}/OUT`GATE)")).IsEqualTo("refused");
		await Assert.That(await Eval($"get({obj}/OUT`MIDDLE)")).IsEmpty()
			.Because("the innermost break stops the list that included it");
		await Assert.That(await Eval($"get({obj}/OUT`REACHED)")).IsEmpty()
			.Because("and that list's break is returned again, stopping the one above it too");
	}

	/// <summary>A chain short-circuits at the failing link, and stops the list that ran the chain.</summary>
	[Test]
	public async ValueTask AChain_ShortCircuits_AndStopsTheIncludingList()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclChain");
		await Cmd($"&INC`ONE {obj}=&OUT`ONE %!=one ran");
		await Cmd($"&INC`TWO {obj}=@assert 0=&OUT`GATE %!=refused");
		await Cmd($"&INC`THREE {obj}=&OUT`THREE %!=three ran");
		await Cmd($"&RUN {obj}=@include/chain %!/INC`ONE %!/INC`TWO %!/INC`THREE; &OUT`REACHED %!=the caller kept going");
		await Cmd($"@trigger {obj}/RUN");

		await Assert.That(await Eval($"get({obj}/OUT`ONE)")).IsEqualTo("one ran");
		await Assert.That(await Eval($"get({obj}/OUT`GATE)")).IsEqualTo("refused");
		await Assert.That(await Eval($"get({obj}/OUT`THREE)")).IsEmpty()
			.Because("a failing link short-circuits the rest of the chain");
		await Assert.That(await Eval($"get({obj}/OUT`REACHED)")).IsEmpty()
			.Because("and the chain's break stops the list that ran the chain");
	}

	/// <summary>
	/// The two-argument form runs its action then breaks. The break belongs to the list the
	/// <c>@assert</c> is in — the included one — so it reaches the caller.
	/// </summary>
	[Test]
	public async ValueTask AFailingAssertWithAnAction_RunsTheActionThenStopsBothLists()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclDeep");
		await Cmd($"&INC`GATE {obj}=@assert 0={{&OUT`GATE %!=refused}}; &OUT`AFTERGATE %!=the guard kept going");
		await Cmd($"&RUN {obj}=@include %!/INC`GATE; &OUT`REACHED %!=the caller kept going");
		await Cmd($"@trigger {obj}/RUN");

		await Assert.That(await Eval($"get({obj}/OUT`GATE)")).IsEqualTo("refused");
		await Assert.That(await Eval($"get({obj}/OUT`AFTERGATE)")).IsEmpty()
			.Because("the failing @assert stops its own list at the point it fires");
		await Assert.That(await Eval($"get({obj}/OUT`REACHED)")).IsEmpty()
			.Because("and that break is the included list's, so it reaches the caller");
	}
}
