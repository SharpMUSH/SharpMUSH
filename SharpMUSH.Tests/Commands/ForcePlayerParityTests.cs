using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@force</c> and <c>@trigger</c> aimed at a connected player run the code as that player, and their
/// refusals read as PennMUSH's do (<c>do_force</c>, <c>src/wiz.c:631-667</c>; <c>do_trigger</c>,
/// <c>src/set.c:1272-1350</c>; <c>do_pcreate</c>, <c>src/wiz.c:108-116</c>). Expected lines are the
/// PennMUSH 1.8.8 p0 (rev <c>80a1d5b</c>) transcript of <c>tools/parity/scenarios/20-admin-commands.scn</c>.
/// </summary>
public class ForcePlayerParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	private Task Run(long handle, string command)
		=> Parser.CommandParse(handle, Connections, MarkupText.Plain(command)).AsTask();

	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services,
			Factory.Services.GetRequiredService<IMediator>(), Connections, prefix);

	/// <summary>A wizard and a player standing in a room of their own.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Wizard, TestIsolationHelpers.TestPlayer Target)> Setup(string prefix)
	{
		var wizard = await Player($"{prefix}Wiz");
		var target = await Player($"{prefix}Tgt");
		var room = (await Parser.CommandParse(1, Connections, MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"))).Message.ToPlainText();
		await Run(1, $"@set {wizard.DbRef}=WIZARD");
		await Run(1, $"@teleport {wizard.DbRef}={room}");
		await Run(1, $"@teleport {target.DbRef}={room}");
		return (wizard, target);
	}

	[Test]
	public async Task ForcedSayIsSpokenByTheForcedPlayer()
	{
		var (wizard, target) = await Setup("ForceSay");
		var text = TestIsolationHelpers.GenerateUniqueName("forced");

		await Run(wizard.Handle, $"@force *{target.Name}=say {text}");

		await Assert.That(Factory.Notifications.For(target.DbRef)).Contains($"You say, \"{text}\"");
		await Assert.That(Factory.Notifications.For(wizard.DbRef)).Contains($"{target.Name} says, \"{text}\"");
	}

	/// <summary>
	/// <c>/noeval</c> on a command with an <c>=</c> leaves the right side raw (<c>command_parse</c>,
	/// <c>src/command.c:1436-1446</c>), so the forced player is the one who evaluates it: <c>%!</c> is
	/// the forced player and <c>%#</c> the forcer. Without the switch the forcer evaluates it first (#1389).
	/// </summary>
	[Test]
	public async Task ForceNoEvalLeavesTheCommandForTheForcedPlayerToEvaluate()
	{
		var (wizard, target) = await Setup("ForceNoEval");
		var raw = TestIsolationHelpers.GenerateUniqueName("raw");
		var evaluated = TestIsolationHelpers.GenerateUniqueName("evaluated");

		await Run(wizard.Handle, $"@force/noeval *{target.Name}=think {raw} me:%! en:%#");
		await Run(wizard.Handle, $"@force *{target.Name}=think {evaluated} me:%! en:%#");

		var heard = Factory.Notifications.For(target.DbRef);
		await Assert.That(heard).Contains($"{raw} me:#{target.DbRef.Number} en:#{wizard.DbRef.Number}");
		await Assert.That(heard).Contains($"{evaluated} me:#{wizard.DbRef.Number} en:#{wizard.DbRef.Number}");
	}

	[Test]
	public async Task TriggeredSayIsSpokenByTheTriggeredPlayer()
	{
		var (wizard, target) = await Setup("TrigSay");
		var text = TestIsolationHelpers.GenerateUniqueName("triggered");

		await Run(wizard.Handle, $"&TRIG *{target.Name}=say {text} %0");
		await Run(wizard.Handle, $"@trigger *{target.Name}/TRIG=1");

		await Factory.Notifications.WaitForAsync(target.DbRef, $"You say, \"{text} 1\"");
		var heard = Factory.Notifications.For(wizard.DbRef).ToList();
		var triggered = heard.IndexOf($"{target.Name} - Triggered.");
		await Assert.That(triggered).IsGreaterThanOrEqualTo(0);
		await Assert.That(heard.IndexOf($"{target.Name} says, \"{text} 1\"")).IsGreaterThan(triggered);
	}

	/// <summary>A refused <c>@trigger/match</c> is not also reported as triggered.</summary>
	[Test]
	public async Task TriggerMatchWithoutAStringIsNotReportedAsTriggered()
	{
		var (wizard, target) = await Setup("TrigNoMatch");

		await Run(wizard.Handle, $"&TRIG *{target.Name}=say never");
		await Run(wizard.Handle, $"@trigger/match *{target.Name}/TRIG");

		await Assert.That(Factory.Notifications.For(wizard.DbRef)).Contains("You must provide a string to match when using /match.");
		await Assert.That(Factory.Notifications.For(wizard.DbRef)).DoesNotContain($"{target.Name} - Triggered.");
	}

	/// <summary>match_controlled says "Permission denied." for a player the forcer cannot control; do_force adds "Sorry.".</summary>
	[Test]
	public async Task ForcingAnUncontrolledPlayerIsDeniedThenSorry()
	{
		var (wizard, _) = await Setup("ForceDeny");
		var mortal = await Player("ForceDenyMortal");

		await Run(mortal.Handle, $"@force *{wizard.Name}=say hi");

		var heard = Factory.Notifications.For(mortal.DbRef).ToList();
		var denied = heard.IndexOf("Permission denied.");
		await Assert.That(denied).IsGreaterThanOrEqualTo(0);
		await Assert.That(heard.IndexOf("Sorry.")).IsGreaterThan(denied);
		await Assert.That(Factory.Notifications.For(wizard.DbRef)).DoesNotContain("You say, \"hi\"");
	}

	[Test]
	public async Task PcreateByAMortalHasNoPowerOverBodyAndMind()
	{
		var mortal = await Player("PcreateMortal");
		var name = TestIsolationHelpers.GenerateUniqueName("Mallory");

		await Run(mortal.Handle, $"@pcreate {name}=x");

		await Assert.That(Factory.Notifications.For(mortal.DbRef)).Contains("You do not have the power over body and mind!");
		await Assert.That(Factory.Notifications.For(mortal.DbRef)).DoesNotContain("Permission denied.");
	}
}
