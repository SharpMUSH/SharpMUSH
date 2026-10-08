using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@role</c>, <c>roles()</c>, <c>hasrole()</c> and <c>permission()</c> against the live server: the
/// commands write through the role management rules and the functions read the same answer every
/// capability gate uses.
/// </summary>
public class RoleCommandTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;
	private TestIsolationHelpers.TestPlayer _unlinked = null!;
	private readonly string _slug = "r" + Guid.NewGuid().ToString("N")[..12];

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RoleWiz");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RoleMortal");
		_unlinked = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RoleNoAcct");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
		await LinkAsync(_wizard);
		await LinkAsync(_mortal);
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _wizard, _mortal, _unlinked })
			await ConnectionService.Disconnect(player.Handle);
	}

	private async Task LinkAsync(TestIsolationHelpers.TestPlayer player)
	{
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("acct"), null, "role-password-1"))
			.Expect<SharpAccount>();
		var character = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
		await Accounts.LinkCharacterAsync(account.Id!, character.Object.DBRef);
	}

	private Task<string> As(TestIsolationHelpers.TestPlayer player, string command) => CmdAs(player.DbRef, player.Handle, command);

	[Test]
	public async Task AnyoneCanListRoles()
	{
		var output = await As(_mortal, "@role/list");
		await Assert.That(output).Contains("moderator");
		await Assert.That(output).Contains("everyone");
		await Assert.That(await As(_mortal, "@role moderator")).Contains("Allows: ");
	}

	[Test]
	public async Task WizardCreatesAllowsAndAssignsARole()
	{
		await Assert.That(await As(_wizard, $"@role/create {_slug}=Staff/Storyteller")).Contains("created");
		await Assert.That(await As(_wizard, $"@role/priority {_slug}=14")).Contains("priority 14");
		await Assert.That(await As(_wizard, $"@role/allow {_slug}=wiki.delete")).Contains("allows wiki.delete");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},wiki.delete)")).IsEqualTo("0");

		await Assert.That(await As(_wizard, $"@role/assign {_mortal.Name}={_slug}")).Contains($"now holds {_slug}");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasrole(*{_mortal.Name},{_slug})")).IsEqualTo("1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},wiki.delete)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"roles(*{_mortal.Name})")).IsEqualTo($"{_slug} player everyone");

		await As(_wizard, $"@role/unassign {_mortal.Name}={_slug}");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasrole(*{_mortal.Name},{_slug})")).IsEqualTo("0");
		await Assert.That(await As(_wizard, $"@role/delete {_slug}")).Contains("deleted");
	}

	[Test]
	public async Task ObjectOverrideBeatsTheirRoles()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},media.upload)")).IsEqualTo("1");
		await Assert.That(await As(_wizard, $"@permission/deny {_mortal.Name}=media.upload")).Contains("denies media.upload");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},media.upload)")).IsEqualTo("0");
		await Assert.That(await As(_wizard, $"@role/player {_mortal.Name}")).Contains("deny media.upload");
		await As(_wizard, $"@permission/clear {_mortal.Name}=media.upload");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},media.upload)")).IsEqualTo("1");
	}

	[Test]
	public async Task AccountOverrideAndRoleReachTheCharacter()
	{
		await Assert.That(await As(_wizard, $"@permission/allow/account {_mortal.Name}=wiki.delete")).Contains("(account ");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},wiki.delete)")).IsEqualTo("1");
		await As(_wizard, $"@permission/clear/account {_mortal.Name}=wiki.delete");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},wiki.delete)")).IsEqualTo("0");

		await Assert.That(await As(_wizard, $"@role/assign/account {_mortal.Name}=helper")).Contains("now holds helper");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasrole(*{_mortal.Name},helper)")).IsEqualTo("1");
		await Assert.That(await As(_wizard, $"@role/player {_mortal.Name}")).Contains("Helper (12, account)");
		await As(_wizard, $"@role/unassign/account {_mortal.Name}=helper");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasrole(*{_mortal.Name},helper)")).IsEqualTo("0");
	}

	[Test]
	public async Task MortalsCannotManageRoles()
	{
		await Assert.That(await As(_mortal, $"@role/assign {_wizard.Name}=helper")).Contains("roles.admin");
		await Assert.That(await As(_mortal, $"@role/player {_wizard.Name}")).Contains("players.view");
		await Assert.That(await As(_mortal, "@role/player")).Contains("Holds: ");
	}

	[Test]
	public async Task WizardCannotRaiseARoleToItsOwnRank()
	{
		await As(_wizard, $"@role/create {_slug}=Staff");
		await Assert.That(await As(_wizard, $"@role/priority {_slug}=30")).Contains("not below your highest role");
		await Assert.That(await As(_wizard, $"@role/allow {_slug}=server.admin")).Contains("do not hold");
		await As(_wizard, $"@role/delete {_slug}");
	}

	[Test]
	public async Task PlayersWithoutAnAccountHoldTheirOwnRoles()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"roles(*{_unlinked.Name})")).IsEqualTo("player everyone");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_unlinked.Name},wiki.read)")).IsEqualTo("1");
		await Assert.That(await As(_wizard, $"@role/assign {_unlinked.Name}=helper")).Contains("now holds helper");
		await Assert.That(await EvalAs(_wizard.DbRef, $"roles(*{_unlinked.Name})")).IsEqualTo("helper player everyone");
		await Assert.That(await As(_wizard, $"@role/assign/account {_unlinked.Name}=helper")).Contains("with an account");
		await As(_wizard, $"@role/unassign {_unlinked.Name}=helper");
	}

	[Test]
	public async Task UnknownPermissionIsAnError()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission(*{_mortal.Name},no.such)")).IsEqualTo("#-1 NO SUCH PERMISSION");
		await Assert.That(await As(_wizard, "@role/allow helper=no.such")).Contains("Unknown permission");
	}
}
