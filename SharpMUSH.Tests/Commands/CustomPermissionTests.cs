using System.Text.RegularExpressions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A permission the game defines for itself with <c>@permission/define</c>: granted through a role, read by
/// <c>permission()</c>, <c>PERM^</c> locks and function restrictions, and gone again with <c>@permission/undefine</c>.
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
		await Heard(_wizard, $"@permission/undefine {_scope}");
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

		await Assert.That(await Heard(_wizard, $"@permission/define {_scope}=Staff/Finish any scene")).Contains($"Permission {_scope} defined");
		await Assert.That(Regex.IsMatch(await Heard(_wizard, "@permission"), $@"{Regex.Escape(_scope)}\s+Finish any scene")).IsTrue();
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"permission(#1,{_scope})")).IsEqualTo("1");

		await Heard(_wizard, $"@role/create {_role}=Staff");
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
		await Heard(_wizard, $"@permission/define {_scope}=Staff");
		await Assert.That(await Heard(_wizard, $"@permission/allow {Ref(_mortal)}={_scope}")).DoesNotContain("Unknown permission");
		await Assert.That(await AsWizard($"permission({Ref(_mortal)},{_scope})")).IsEqualTo("1");
	}

	[Test]
	public async Task UndefiningTakesItFromEveryHolder()
	{
		await Heard(_wizard, $"@permission/define {_scope}=Staff");
		await Heard(_wizard, $"@role/create {_role}=Staff");
		await Heard(_wizard, $"@role/allow {_role}={_scope}");
		await Heard(_wizard, $"@role/assign {Ref(_helper)}={_role}");
		await Heard(_wizard, $"@permission/allow {Ref(_mortal)}={_scope}");

		await Assert.That(await Heard(_wizard, $"@permission/undefine {_scope}")).Contains("removed");
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("#-1 NO SUCH PERMISSION");
		await Assert.That(await Heard(_wizard, $"@role {_role}")).DoesNotContain(_scope);

		// Defining the name again starts from nothing: the old settings went with it.
		await Heard(_wizard, $"@permission/define {_scope}=Staff");
		await Assert.That(await AsWizard($"permission({Ref(_helper)},{_scope})")).IsEqualTo("0");
		await Assert.That(await AsWizard($"permission({Ref(_mortal)},{_scope})")).IsEqualTo("0");
	}

	[Test]
	public async Task CommandRestrictionTakesAPermissionByName()
	{
		await Heard(_wizard, $"@permission/define {_scope}=Staff");
		await Heard(_wizard, $"@permission/allow {Ref(_helper)}={_scope}");
		var command = "CP" + _role.ToUpperInvariant();
		await Heard(_wizard, $"@command/clone think={command}");

		await Assert.That(await Heard(_wizard, $"@command/restrict {command}={_scope}")).Contains($"PERM^{_scope}");

		await Assert.That(await Heard(_helper, $"{command} closer {_role}")).Contains($"closer {_role}");
		await Assert.That(await Heard(_mortal, $"{command} closer {_role}")).DoesNotContain($"closer {_role}");
	}

	[Test]
	public async Task RolesAndPermissionsTakeACategory()
	{
		var category = "Cat " + _role;
		await Assert.That(await Heard(_wizard, $"@role/create {_role}")).Contains("Usage: @role/create <role>=<category>");
		await Assert.That(await Heard(_wizard, $"@role/create {_role}={category}/Closers")).Contains($"No role category named '{category}'. Create the category first: @role/category/create {category}=");
		await Assert.That(await Heard(_wizard, $"@role/category/create {category}=People who close scenes")).Contains($"Role category {category} created");
		await Assert.That(await Heard(_wizard, $"@role/create {_role}={category.ToUpperInvariant()}/Closers")).Contains($"created in {category}");
		await Assert.That(await Heard(_wizard, $"@role {_role}")).Contains($"Category: {category}");

		// Permissions have a list of their own.
		await Assert.That(await Heard(_wizard, $"@permission/define {_scope}")).Contains("Usage: @permission/define <permission>=<category>");
		await Assert.That(await Heard(_wizard, $"@permission/define {_scope}={category}/Finish any scene")).Contains($"No permission category named '{category}'. Create the category first: @permission/category/create {category}=");
		await Assert.That(await Heard(_wizard, $"@permission/category/create {category}=Scene permissions")).Contains($"Permission category {category} created");
		await Heard(_wizard, $"@permission/define {_scope}=Staff/Finish any scene");
		await Assert.That(await Heard(_wizard, $"@permission/category {_scope}={category}")).Contains($"{_scope} is now in {category}");
		await Assert.That(Regex.IsMatch(await Heard(_wizard, "@permission"), $@"{Regex.Escape(category)}\s+{Regex.Escape(_scope)}\s+Finish any scene")).IsTrue();
		await Assert.That(await Heard(_wizard, $"@permission/category wiki.read={category}")).Contains("built-in");

		await Assert.That(await Heard(_wizard, "@role/categories")).Contains("Role categories").And.Contains("People who close scenes").And.DoesNotContain("Scene permissions");
		await Assert.That(await Heard(_wizard, "@permission/categories")).Contains("Permission categories").And.Contains("Scene permissions");
		var info = await Heard(_wizard, $"@permission {_scope}");
		await Assert.That(Regex.IsMatch(info, $@"Category:\s+{Regex.Escape(category)}")).IsTrue();
		await Assert.That(Regex.IsMatch(info, @"Allowed by:\s+none")).IsTrue();
		await Assert.That(await Heard(_wizard, $"@role/category/delete {category}")).Contains($"still holds {_role}");
		await Assert.That(await Heard(_wizard, $"@permission/category/delete {category}")).Contains($"still holds {_scope}");

		await Assert.That(await Heard(_wizard, $"@role/category/describe {category}=Closers")).Contains($"Role category {category}: Closers");
		var renamed = category + "x";
		await Assert.That(await Heard(_wizard, $"@role/category/rename {category}={renamed}")).Contains($"is now {renamed}");
		await Assert.That(await Heard(_wizard, $"@role {_role}")).Contains($"Category: {renamed}");
		await Assert.That(Regex.IsMatch(await Heard(_wizard, "@permission"), $@"{Regex.Escape(category)}\s+{Regex.Escape(_scope)}")).IsTrue();
		await Assert.That(await Heard(_wizard, $"@role/category {_role}=Staff")).Contains("is now in Staff");
		await Assert.That(await Heard(_wizard, $"@role/category/delete {renamed}")).Contains("deleted");
		await Heard(_wizard, $"@permission/category {_scope}=Staff");
		await Assert.That(await Heard(_wizard, $"@permission/category/delete {category}")).Contains($"Permission category {category} deleted");
		await Assert.That(await Heard(_wizard, "@role/categories")).DoesNotContain(category);
		await Assert.That(await Heard(_wizard, "@permission/categories")).DoesNotContain(category);
	}

	[Test]
	public async Task CategoryCommandsNeedARolesAdmin()
	{
		await Assert.That(await Heard(_mortal, $"@role/category/create Cat{_role}=Nope")).Contains("roles.admin");
		await Assert.That(await Heard(_wizard, "@role/category/create Café=Nope")).Contains("a player name may use");
	}

	[Test]
	[Arguments("rolename", "scene-runner", "1")]
	[Arguments("rolename", "Scene Runner", "0")]
	[Arguments("rolename", "a/b", "0")]
	[Arguments("rolecategory", "Scene staff", "1")]
	[Arguments("rolecategory", "Staff/Helpers", "0")]
	[Arguments("rolecategory", "Staff|Helpers", "0")]
	[Arguments("rolecategory", "me", "0")]
	[Arguments("permission", "scene.close", "1")]
	[Arguments("permission", "close", "0")]
	[Arguments("permission", "Scene.Close", "0")]
	public async Task ValidChecksRoleNames(string type, string name, string expected)
		=> await Assert.That(await AsWizard($"valid({type},{name})")).IsEqualTo(expected);

	[Test]
	public async Task OnlyARolesAdminDefinesOne()
	{
		await Assert.That(await Heard(_mortal, $"@permission/define {_scope}=Staff")).Contains("roles.admin");
		await Assert.That(await AsWizard($"permission(#1,{_scope})")).IsEqualTo("#-1 NO SUCH PERMISSION");
		await Assert.That(await Heard(_wizard, "@permission/define wiki.read=Staff")).Contains("built-in");
		await Assert.That(await Heard(_wizard, "@permission/define game.nope=Staff")).Contains("not under");
	}
}
