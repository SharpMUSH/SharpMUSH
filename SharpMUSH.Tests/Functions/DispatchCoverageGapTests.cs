using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Functions that some test source named but no test ever dispatched, found by comparing the
/// parser's invocation telemetry with <see cref="FunctionCoverage"/>'s mention scan (#974).
///
/// <para>Every expected value for a PennMUSH function here is what PennMUSH printed for the same
/// call in the parity harness's <c>sc.coverage-gaps</c> case (<c>tools/parity/scenarios/30-softcode.scn</c>).
/// Where the two servers disagree, the call is not pinned here: the difference is an open parity bug
/// in <c>tools/parity/baseline.json</c>, and the fix brings its own test. <c>isapproved()</c> and
/// <c>locale()</c> have no PennMUSH counterpart and are checked against the help that describes them.</para>
/// </summary>
public class DispatchCoverageGapTests : ServerTestBase
{
	[Test]
	[Arguments("floor(2.7)", "2")]
	[Arguments("floor(-2.7)", "-3")]
	[Arguments("floor(3)", "3")]
	[Arguments("floor(abc)", "#-1 ARGUMENT MUST BE NUMBER")]
	[Arguments("bound(5,1,10)", "5")]
	[Arguments("bound(-5,1,10)", "1")]
	[Arguments("bound(15,1)", "15")]
	[Arguments("bound(1.5,1,2)", "1.5")]
	[Arguments("art(apple)", "an")]
	[Arguments("art(banana)", "a")]
	[Arguments("art(Egg)", "an")]
	[Arguments("art(8)", "a")]
	[Arguments("ordinal(1)", "first")]
	[Arguments("ordinal(2)", "second")]
	[Arguments("ordinal(3)", "third")]
	[Arguments("ordinal(4)", "fourth")]
	[Arguments("ordinal(11)", "eleventh")]
	[Arguments("ordinal(12)", "twelfth")]
	[Arguments("ordinal(21)", "twenty-first")]
	[Arguments("ordinal(0)", "zeroth")]
	public async Task AgreesWithPennMUSH(string call, string expected)
	{
		await Assert.That(await Eval(call)).IsEqualTo(expected);
	}

	[Test]
	public async Task XgetReadsTheNamedAttribute()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "XgetThing");
		await Cmd($"&SIZE {thing}=42");

		await Assert.That(await Eval($"xget({thing},SIZE)")).IsEqualTo("42");
	}

	[Test]
	public async Task PlayerNamesWhoIsOnYourOwnPort()
	{
		await Assert.That(await Eval("player(first(ports(me)))")).IsEqualTo("#1");
	}

	/// <summary>
	/// <c>help isapproved()</c>: 1 for a non-guest that is royalty or above or holds the <c>approved</c> role.
	/// </summary>
	[Test]
	public async Task IsApprovedFollowsTheApprovedRole()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "Approval");

		await Assert.That(await Eval($"isapproved({player})")).IsEqualTo("0");

		await Cmd($"@role/assign {player}=approved");

		await Assert.That(await Eval($"isapproved({player})")).IsEqualTo("1");
	}

	/// <summary>
	/// <c>locale()</c> reads the per-connection setting <c>help @locale</c> describes: the tag the
	/// connection chose, <c>en</c> when it chose none. Each case uses a fresh handle; handle 1 is
	/// shared by the whole session.
	/// </summary>
	[Test]
	[Arguments(null, "en")]
	[Arguments("fr", "fr")]
	public async Task LocaleIsTheConnectionsChoice(string? chosen, string expected)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Locale");
		if (chosen is not null)
			ConnectionService.Update(player.Handle, "Locale", chosen);

		var result = await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.FunctionParse(MString.Plain("locale()"));

		await Assert.That(result?.Message.ToPlainText()).IsEqualTo(expected);
	}
}
