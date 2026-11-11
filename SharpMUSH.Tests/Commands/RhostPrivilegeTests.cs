using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What RhostMUSH's privileges land on here beyond the staff and everyday permissions: <c>game.hide.roles</c> (HIDEBIT),
/// the <c>wizard_dark</c> attribute flag (AF_DARK), the See_OOB power, and the depower functions.
/// </summary>
public class RhostPrivilegeTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HideWiz");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HideMortal");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
	}

	[After(Test)]
	public async Task Disconnect()
	{
		await ConnectionService.Disconnect(_wizard.Handle);
		await ConnectionService.Disconnect(_mortal.Handle);
	}

	private string Wizard => $"#{_wizard.DbRef.Number}";
	private string Mortal => $"#{_mortal.DbRef.Number}";

	[Test]
	public async Task HideRolesHidesAWizardsRolesFromLowerRanks()
	{
		await Assert.That(await EvalAs(_mortal.DbRef, $"flags({Wizard})")).Contains("W");
		await Assert.That(await EvalAs(_mortal.DbRef, $"roles({Wizard})")).Contains("wizard");

		await Cmd($"@permission/allow {Wizard}={PortalPermission.HideRoles}");

		await Assert.That(await EvalAs(_mortal.DbRef, $"flags({Wizard})")).DoesNotContain("W");
		await Assert.That(await EvalAs(_mortal.DbRef, $"lflags({Wizard})")).DoesNotContain("WIZARD");
		await Assert.That(await EvalAs(_mortal.DbRef, $"roles({Wizard})")).IsEqualTo("everyone");
		await Assert.That(await EvalAs(_mortal.DbRef, $"hasflag({Wizard},WIZARD)")).IsEqualTo("1")
			.Because("a check still answers; only what is shown is hidden");

		await Assert.That(await EvalAs(_wizard.DbRef, $"flags({Wizard})")).Contains("W")
			.Because("nothing hides from itself");
		await Assert.That(await Eval($"lflags({Wizard})")).Contains("WIZARD")
			.Because("nothing hides from #1");
	}

	[Test]
	public async Task HideRolesWithAReachHidesFromPeersItReaches()
	{
		var peer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HidePeer");
		try
		{
			await Cmd($"@set #{peer.DbRef.Number}=WIZARD");
			await Cmd($"@permission/allow {Wizard}={PortalPermission.HideRoles}");
			await Assert.That(await EvalAs(peer.DbRef, $"lflags({Wizard})")).Contains("WIZARD")
				.Because("held without a reach, it hides only from viewers ranked below its holder");

			await Cmd($"@permission/allow {Wizard}={PortalPermission.HideRoles}/wizard");
			await Assert.That(await EvalAs(peer.DbRef, $"lflags({Wizard})")).DoesNotContain("WIZARD");
		}
		finally
		{
			await ConnectionService.Disconnect(peer.Handle);
		}
	}

	[Test]
	public async Task WizardDarkHidesAnAttributeFromAllButGod()
	{
		var thing = $"#{DBRef.Parse(await CmdAs(_wizard.DbRef, _wizard.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName("DarkBox")}")).Number}";
		var marker = TestIsolationHelpers.GenerateUniqueName("dark");
		await Cmd($"&SECRET {thing}={marker}");
		await Cmd($"@set {thing}/SECRET=wizard_dark");

		await Assert.That(await Eval($"get({thing}/SECRET)")).IsEqualTo(marker);
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({thing}/SECRET)")).IsNotEqualTo(marker)
			.Because("wizard_dark hides from wizards too");
		await Assert.That(await EvalAs(_wizard.DbRef, $"lattr({thing})")).DoesNotContain("SECRET");

		await Assert.That(await EvalAs(_wizard.DbRef, $"u({thing}/SECRET)")).IsNotEqualTo(marker)
			.Because("evaluating it reads it too");
		await Assert.That(await EvalAs(_wizard.DbRef, $"default({thing}/SECRET,none)")).IsNotEqualTo(marker);

		await CmdAs(_wizard.DbRef, _wizard.Handle, $"&OPEN {thing}=x");
		await CmdAs(_wizard.DbRef, _wizard.Handle, $"@set {thing}/OPEN=wizard_dark");
		await Assert.That(await Eval($"lflags({thing}/OPEN)")).DoesNotContain("wizard_dark")
			.Because("only God sets wizard_dark");
	}

	[Test]
	public async Task OnlyGodGivesWizardDarkThroughTheAttributeTable()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("DARKENTRY").ToUpperInvariant();
		var thing = $"#{DBRef.Parse(await CmdAs(_wizard.DbRef, _wizard.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName("DarkTable")}")).Number}";
		await Cmd($"&{name} {thing}=hidden");
		await Cmd($"@set {thing}/{name}=wizard_dark");

		await CmdAs(_wizard.DbRef, _wizard.Handle, $"@attribute/access {name}=wizard_dark");
		await CmdAs(_wizard.DbRef, _wizard.Handle, $"&{name} me=new");
		await Assert.That(await Eval($"lflags({Wizard}/{name})")).DoesNotContain("wizard_dark")
			.Because("only God makes every new copy wizard_dark");

		await CmdAs(_wizard.DbRef, _wizard.Handle, $"@attribute/access/retroactive {name}=none");
		await Assert.That(await Eval($"lflags({thing}/{name})")).Contains("wizard_dark")
			.Because("a retroactive pass by anyone but God leaves wizard_dark where it is");
	}

	[Test]
	public async Task HideRolesHidesThePermissionReport()
	{
		await Cmd($"@permission/allow {Wizard}={PortalPermission.HideRoles}");
		await Cmd($"@permission/allow {Mortal}={PortalPermission.PlayersView}");
		await Assert.That(await CmdAs(_mortal.DbRef, _mortal.Handle, $"@role/player {Wizard}")).IsNotEqualTo(NullResult);
		await Assert.That(Notifications.For(_mortal.DbRef).Any(line => line.Contains("hides its roles from you"))).IsTrue();
	}

	[Test]
	public async Task SeeOobReadsAnotherConnectionsGmcpModules()
	{
		await Assert.That(await EvalAs(_mortal.DbRef, $"gmcpsupports({Wizard})")).StartsWith("#-1");
		await Cmd($"@power {Mortal}=See_OOB");
		await Assert.That(await EvalAs(_mortal.DbRef, $"gmcpsupports({Wizard})")).DoesNotStartWith("#-1");
	}

	[Test]
	public async Task DepowersAreTheirDenyOverrides()
	{
		await Assert.That(await Eval($"ldepowers({Mortal})")).IsEqualTo(string.Empty);
		await Cmd($"@permission/deny {Mortal}={PortalPermission.PlayerPage}");

		await Assert.That(await Eval($"hasdepower({Mortal},page)")).IsEqualTo("1");
		await Assert.That(await Eval($"hasdepower({Mortal},BOOT)")).IsEqualTo("0");
		await Assert.That(await Eval($"hasdepower({Mortal},MASTER)")).IsEqualTo("0")
			.Because("a depower SharpMUSH does not import is never held");
		await Assert.That(await Eval($"hasdepower({Mortal},NO_SUCH_THING)")).IsEqualTo(ErrorMessages.Returns.NoSuchDepower);
		await Assert.That(await Eval($"ldepowers({Mortal})")).IsEqualTo("PAGE");
	}

	[Test]
	public async Task ImportsMapHidebitAndDarkAttributes()
	{
		await Assert.That(RhostNameTables.Powers.Single(p => p.Name == "HIDEBIT").Scopes).IsEquivalentTo([PortalPermission.HideRoles]);
		await Assert.That(RhostNameTables.Powers.Single(p => p.Name == "HIDEBIT").Ranked).IsTrue();
		await Assert.That(RhostNameTables.AttributeFlags.Single(f => f.Name == "DARK").Flag).IsEqualTo("wizard_dark");
		await Assert.That(TinyMuxNames.AttributeFlags[0x2].Flags).IsEquivalentTo(["wizard_dark"]);
	}
}
