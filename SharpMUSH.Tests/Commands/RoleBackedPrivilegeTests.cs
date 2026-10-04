using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Every role-backed flag and power, through each way softcode reaches it: <c>@set</c>/<c>@power</c> on,
/// the read functions, the lock keys, <c>examine</c>, <c>@search</c>, and off again. A name handled in
/// one place and missed in another fails here.
/// </summary>
public class RoleBackedPrivilegeTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _target = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RbWiz");
		_target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RbTarget");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _wizard, _target })
			await ConnectionService.Disconnect(player.Handle);
	}

	private Task<string> AsWizard(string expression) => EvalAs(_wizard.DbRef, expression);

	private Task<string> As(string command) => CmdAs(_wizard.DbRef, _wizard.Handle, command);

	private string Target => $"#{_target.DbRef.Number}";

	[Test]
	[Arguments("WIZARD", "W", "wizard")]
	[Arguments("ROYALTY", "r", "royalty")]
	public async Task RoleFlagIsTheRole(string flag, string letter, string role)
	{
		await As($"@set {Target}={flag}");
		await Assert.That(await AsWizard($"hasflag({Target},{flag})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"hasrole({Target},{role})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"strmatch(flags({Target}),*{letter}*)")).IsEqualTo("1");
		await Assert.That(await AsWizard($"testlock(FLAG^{flag},{Target})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"testlock(ROLE^{role},{Target})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"member(lsearch(all,lflags,{flag}),{Target})")).IsNotEqualTo("0");
		await Assert.That(await As($"examine {Target}")).Contains($"Roles: {role}");

		await As($"@set {Target}=!{flag}");
		await Assert.That(await AsWizard($"hasflag({Target},{flag})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"hasrole({Target},{role})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"testlock(FLAG^{flag},{Target})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"member(lsearch(all,lflags,{flag}),{Target})")).IsEqualTo("0");
	}

	[Test]
	[Arguments("See_All", "game.see_all")]
	[Arguments("tel_anywhere", "game.tport_anywhere")]
	[Arguments("Builder", "game.builder")]
	[Arguments("Guest", "game.guest")]
	public async Task PowerIsAPermission(string power, string scope)
	{
		await As($"@power {Target}={power}");
		await Assert.That(await AsWizard($"haspower({Target},{power})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"permission({Target},{scope})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"testlock(POWER^{power},{Target})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"testlock(PERM^{scope},{Target})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"words(powers({Target}))")).IsEqualTo("1");
		await Assert.That(await AsWizard($"member(lsearch(all,powers,{power}),{Target})")).IsNotEqualTo("0");
		await Assert.That(await As($"examine {Target}")).Contains("Powers: ");

		await As($"@power {Target}=!{power}");
		await Assert.That(await AsWizard($"haspower({Target},{power})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"permission({Target},{scope})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"testlock(POWER^{power},{Target})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"powers({Target})")).IsEqualTo("");
	}

	[Test]
	public async Task WizardRoleBringsWizardPrivilegeButNoPowers()
	{
		await Assert.That(await AsWizard($"haspower({_wizard.DbRef},See_All)")).IsEqualTo("0");
		await Assert.That(await AsWizard($"hasflag({_wizard.DbRef},ROYALTY)")).IsEqualTo("0");
		await Assert.That(await AsWizard($"controls({_wizard.DbRef},{Target})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"controls({Target},{_wizard.DbRef})")).IsEqualTo("0");
	}

	[Test]
	public async Task RoleThatAllowsAPowerGivesItToEveryHolder()
	{
		var slug = "r" + Guid.NewGuid().ToString("N")[..12];
		await As($"@role/create {slug}");
		await As($"@role/allow {slug}=game.see_all");
		await As($"@role/assign {Target}={slug}");
		await Assert.That(await AsWizard($"haspower({Target},See_All)")).IsEqualTo("1");
		await As($"@role/delete {slug}");
		await Assert.That(await AsWizard($"haspower({Target},See_All)")).IsEqualTo("0");
	}

	[Test]
	public async Task ObjectOverrideDeniesWhatARoleAllows()
	{
		await As($"@set {Target}=ROYALTY");
		await As($"@role/deny/object {Target}=game.royalty");
		await Assert.That(await AsWizard($"hasflag({Target},ROYALTY)")).IsEqualTo("0");
		await Assert.That(await As($"examine {Target}")).Contains("Overrides: -game.royalty");
		await As($"@role/clear/object {Target}=game.royalty");
		await Assert.That(await AsWizard($"hasflag({Target},ROYALTY)")).IsEqualTo("1");
		await As($"@set {Target}=!ROYALTY");
	}

	[Test]
	public async Task MortalCannotMakeAWizard()
	{
		var output = await CmdAs(_target.DbRef, _target.Handle, $"@set {_target.DbRef}=WIZARD");
		await Assert.That(output).Contains("Permission denied");
		await Assert.That(await AsWizard($"hasflag({Target},WIZARD)")).IsEqualTo("0");
	}
}
