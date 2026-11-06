using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The staff permissions split out of <c>game.wizard</c> (<c>help roles staff</c>), held through overrides on players
/// who are not wizards, and the protective ones against a wizard.
/// </summary>
public class StaffScopeTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "StaffWiz");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "StaffMortal");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
	}

	[After(Test)]
	public async Task Disconnect()
	{
		await ConnectionService.Disconnect(_wizard.Handle);
		await ConnectionService.Disconnect(_mortal.Handle);
	}

	private Task<string> As(TestIsolationHelpers.TestPlayer player, string command) => CmdAs(player.DbRef, player.Handle, command);

	[Test]
	public async Task NoForceWithAReachKeepsOutForcersItReaches()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("forced");
		await As(_wizard, $"@force {_mortal.Name}=&FORCED me={marker}1");
		await Assert.That(await Eval($"get(#{_mortal.DbRef.Number}/FORCED)")).IsEqualTo($"{marker}1");

		await Assert.That(await As(_wizard, $"@permission/allow {_mortal.Name}=game.noforce")).Contains("allows game.noforce");
		await As(_wizard, $"@force {_mortal.Name}=&FORCED me={marker}2");
		await Assert.That(await Eval($"get(#{_mortal.DbRef.Number}/FORCED)")).IsEqualTo($"{marker}2")
			.Because("held without a reach, game.noforce keeps out only forcers ranked below its holder");

		await Assert.That(await As(_wizard, $"@permission/allow {_mortal.Name}=game.noforce/wizard")).Contains("reaching wizard");
		await As(_wizard, $"@force {_mortal.Name}=&FORCED me={marker}3");
		await Assert.That(await Eval($"get(#{_mortal.DbRef.Number}/FORCED)")).IsEqualTo($"{marker}2");
		await Assert.That(await Cmd($"@force #{_mortal.DbRef.Number}=&FORCED me={marker}4")).IsNotEqualTo(NullResult);
		await Assert.That(await Eval($"get(#{_mortal.DbRef.Number}/FORCED)")).IsEqualTo($"{marker}4")
			.Because("nothing shields anything from #1");
	}

	[Test]
	public async Task OverrideMovementPassesEnterLocks()
	{
		var box = TestIsolationHelpers.GenerateUniqueName("StaffBox");
		var number = DBRef.Parse(await As(_wizard, $"@create {box}")).Number;
		await As(_wizard, $"@set #{number}=ENTER_OK");
		await As(_wizard, $"@lock/enter #{number}=#FALSE");
		var room = await Eval($"loc(#{_mortal.DbRef.Number})");
		await As(_wizard, $"@tel #{number}={room}");

		await As(_mortal, $"enter #{number}");
		await Assert.That(await Eval($"loc(#{_mortal.DbRef.Number})")).DoesNotStartWith($"#{number}:");

		await As(_wizard, $"@permission/allow {_mortal.Name}=game.override.movement");
		await As(_mortal, $"enter #{number}");
		await Assert.That(await Eval($"loc(#{_mortal.DbRef.Number})")).StartsWith($"#{number}:");
	}

	[Test]
	public async Task GameWizardCoversTheStaffPermissions()
	{
		await Assert.That(await Eval($"permission(#{_wizard.DbRef.Number},game.nuke)")).IsEqualTo("1");
		await Assert.That(await Eval($"permission(#{_wizard.DbRef.Number},game.noboot)")).IsEqualTo("0");
		await Cmd($"@permission/deny #{_wizard.DbRef.Number}=game.wizard");
		await Assert.That(await Eval($"permission(#{_wizard.DbRef.Number},game.nuke)")).IsEqualTo("0");
		await Assert.That(await Eval($"permission(#{_wizard.DbRef.Number},game.chown.self)")).IsEqualTo("0");
		await Cmd($"@permission/clear #{_wizard.DbRef.Number}=game.wizard");
		await Assert.That(await Eval($"permission(#{_wizard.DbRef.Number},game.nuke)")).IsEqualTo("1");
	}

	[Test]
	public async Task ChownAllHonoursTheReach()
	{
		await Cmd($"@permission/allow #{_mortal.DbRef.Number}=game.chown.any/player");
		await As(_wizard, $"@create {TestIsolationHelpers.GenerateUniqueName("StaffChown")}");
		await Assert.That(await As(_mortal, $"@chownall #{_wizard.DbRef.Number}")).StartsWith("#-1 PERMISSION DENIED");
		await Assert.That(await Eval($"lsearch(#{_wizard.DbRef.Number})")).IsNotEmpty();
	}
}
