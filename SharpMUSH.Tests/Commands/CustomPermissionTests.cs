using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A permission the game defines for itself with <c>@role/define</c>: granted through a role, read by
/// <c>permission()</c>, <c>PERM^</c> locks and function restrictions, and gone again with <c>@role/undefine</c>.
/// </summary>
public class CustomPermissionTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _helper = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;
	private string _scope = null!;
	private string _role = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "CpWiz");
		_helper = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "CpHelper");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "CpMortal");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
		var unique = Guid.NewGuid().ToString("N")[..10];
		_scope = $"test{unique}.close";
		_role = "r" + unique;
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await Heard(_wizard, $"@role/delete {_role}");
		await Heard(_wizard, $"@role/undefine {_scope}");
		foreach (var player in new[] { _wizard, _helper, _mortal })
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	private async Task<string> Heard(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await CmdAs(who.DbRef, who.Handle, command);
		return string.Join("\n", WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before));
	}

	private Task<string> AsWizard(string expression) => EvalAs(_wizard.DbRef, expression);

	[Test]
	public async Task DefinedPermissionIsGrantedThroughARole()
	{
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("#-1 NO SUCH PERMISSION");

		await Assert.That(await Heard(_wizard, $"@role/define {_scope}=Finish any scene")).Contains($"Permission {_scope} defined");
		await Assert.That(await Heard(_wizard, "@role/scopes")).Contains($"{_scope}  Finish any scene");
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"permission(#1,{_scope})")).IsEqualTo("1");

		await Heard(_wizard, $"@role/create {_role}");
		await Assert.That(await Heard(_wizard, $"@role/allow {_role}={_scope}")).Contains($"allows {_scope}");
		await Heard(_wizard, $"@role/assign {Ref(_helper)}={_role}");

		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope.ToUpperInvariant()})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"testlock(PERM^{_scope},{Ref(_helper)})")).IsEqualTo("1");
		await Assert.That(await AsWizard($"testlock(PERM^{_scope},{Ref(_mortal)})")).IsEqualTo("0");
		await Assert.That(await Heard(_wizard, $"@role/player {Ref(_helper)}")).Contains(_scope);
	}

	[Test]
	public async Task OverrideTakesACustomPermission()
	{
		await Heard(_wizard, $"@role/define {_scope}");
		await Assert.That(await Heard(_wizard, $"@role/allow/object {Ref(_mortal)}={_scope}")).DoesNotContain("Unknown permission");
		await Assert.That(await AsWizard($"permission({Ref(_mortal)},{_scope})")).IsEqualTo("1");
	}

	[Test]
	public async Task UndefiningTakesItFromEveryHolder()
	{
		await Heard(_wizard, $"@role/define {_scope}");
		await Heard(_wizard, $"@role/create {_role}");
		await Heard(_wizard, $"@role/allow {_role}={_scope}");
		await Heard(_wizard, $"@role/assign {Ref(_helper)}={_role}");
		await Heard(_wizard, $"@role/allow/object {Ref(_mortal)}={_scope}");

		await Assert.That(await Heard(_wizard, $"@role/undefine {_scope}")).Contains("removed");
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("#-1 NO SUCH PERMISSION");
		await Assert.That(await Heard(_wizard, $"@role {_role}")).DoesNotContain(_scope);

		// Defining the name again starts from nothing: the old settings went with it.
		await Heard(_wizard, $"@role/define {_scope}");
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"permission({Ref(_mortal)},{_scope})")).IsEqualTo("0");
	}

	[Test]
	public async Task OnlyARolesAdminDefinesOne()
	{
		await Assert.That(await Heard(_mortal, $"@role/define {_scope}")).Contains("roles.admin");
		await Assert.That(await AsWizard($"permission(#1,{_scope})")).IsEqualTo("#-1 NO SUCH PERMISSION");
		await Assert.That(await Heard(_wizard, "@role/define wiki.read")).Contains("built-in");
		await Assert.That(await Heard(_wizard, "@role/define game.nope")).Contains("not under");
	}
}
