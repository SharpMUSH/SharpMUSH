using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The worked example in the softcode skill's "Roles and permissions" section and SharpMUSH.Guide's
/// "Roles and Permissions" guide: an <c>approved</c> role used as a tag, set by a staff command on a wizard
/// global that checks a custom permission, then read back with <c>hasrole()</c>, <c>role^</c> locks and
/// <c>lsearch()</c>. Names are made unique because every test shares one world.
/// </summary>
public class RoleSoftcodeGuideTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _approver = null!;
	private TestIsolationHelpers.TestPlayer _newbie = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;
	private DBRef _global;
	private string _approved = null!;
	private string _approverRole = null!;
	private string _scope = null!;
	private string _category = null!;
	private string _command = null!;

	[Before(Test)]
	public async Task CreateWorld()
	{
		var unique = Guid.NewGuid().ToString("N")[..10];
		_approved = "approved" + unique;
		_approverRole = "approver" + unique;
		_scope = $"chargen{unique}.approve";
		_category = "Status " + unique;
		_command = "+approve" + unique;

		var room = DBRef.Parse((await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("RoleGuideRoom")}")).Trim());
		_approver = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RgApprover", room);
		_newbie = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RgNewbie", room);
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RgMortal", room);

		// The setup a game's staff types once, as the guide gives it.
		await Cmd($"@role/category/create {_category}=Where a character stands in the game");
		await Cmd($"@role/create {_approved}={_category}/Approved");
		await Cmd($"@permission/define {_scope}=Staff/Approve new characters");
		await Cmd($"@role/create {_approverRole}=Staff/Approver");
		await Cmd($"@role/allow {_approverRole}={_scope}");
		await Cmd($"@role/assign #{_approver.DbRef.Number}={_approverRole}");

		_global = DBRef.Parse((await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("RoleGuideGlobal")}")).Trim());
		await Cmd($"@set {_global}=!no_command");
		await Cmd($"@set {_global}=WIZARD");
		await Cmd($"@tel {_global}={room}");
		await Cmd($"&CMD`APPROVE {_global}=${_command} *:" +
			$"@assert permission(%#,{_scope})=@pemit %#=Permission denied.;" +
			"@assert isdbref(setr(who,pmatch(%0)))=@pemit %#=No such player: %0;" +
			$"@break hasrole(%q<who>,{_approved})=@pemit %#=[name(%q<who>)] is already approved.;" +
			$"@role/assign %q<who>={_approved};" +
			$"@assert hasrole(%q<who>,{_approved})=@pemit %#=Could not approve [name(%q<who>)].;" +
			"@pemit %#=[name(%q<who>)] is approved.");
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await Cmd($"@role/delete {_approved}");
		await Cmd($"@role/delete {_approverRole}");
		await Cmd($"@permission/undefine {_scope}");
		await Cmd($"@role/category/delete {_category}");
		await Cmd($"@set {_global}=halt");
		foreach (var player in new[] { _approver, _newbie, _mortal })
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	private async Task<List<string>> Heard(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = Notifications.CountFor(who.DbRef);
		await CmdAs(who.DbRef, who.Handle, command);
		return Notifications.For(who.DbRef).Skip(before).ToList();
	}

	[Test]
	public async Task StaffCommandTagsACharacterWithARole()
	{
		await Assert.That(await Heard(_mortal, $"{_command} {_newbie.Name}")).Contains("Permission denied.");
		await Assert.That(await Eval($"hasrole({Ref(_newbie)},{_approved})")).IsEqualTo("0");

		await Assert.That(await Heard(_approver, $"{_command} {_newbie.Name}")).Contains($"{_newbie.Name} is approved.");
		await Assert.That(await Eval($"hasrole({Ref(_newbie)},{_approved})")).IsEqualTo("1");
		await Assert.That(await Eval($"roles({Ref(_newbie)})")).IsEqualTo($"player {_approved} everyone");
		await Assert.That(await Heard(_approver, $"{_command} {_newbie.Name}")).Contains($"{_newbie.Name} is already approved.");

		await Assert.That(await Eval($"testlock(role^{_approved},{Ref(_newbie)})")).IsEqualTo("1");
		await Assert.That(await Eval($"testlock(role^{_approved},{Ref(_mortal)})")).IsEqualTo("0");
		await Assert.That(await Eval($"lsearch(all,type,player,elock,role^{_approved})")).IsEqualTo(Ref(_newbie));
	}

	[Test]
	public async Task LockSearchTestsEachCandidateNotTheSearcher()
	{
		// @search: "only objects that pass the given lock string". The skill's attribute-tree example leans on it.
		var leaf = $"GROUP`RG{_approved.ToUpperInvariant()}";
		await Cmd($"&{leaf} {Ref(_newbie)}=member");
		await Assert.That(await Eval($"lsearch(all,type,player,elock,{leaf}:*)")).IsEqualTo(Ref(_newbie));
	}

	[Test]
	public async Task GlobalCannotTagAWizard()
	{
		await Cmd($"@set {Ref(_newbie)}=WIZARD");
		await Assert.That(await Heard(_approver, $"{_command} {_newbie.Name}")).Contains($"Could not approve {_newbie.Name}.");
		await Assert.That(await Eval($"hasrole({Ref(_newbie)},{_approved})")).IsEqualTo("0");
		await Cmd($"@set {Ref(_newbie)}=!WIZARD");
	}

	[Test]
	public async Task WizardMakesItsOwnGlobalAWizard()
	{
		// The wizard role is not below a wizard's own, but an object may give a role it holds to a thing it owns.
		await Cmd($"@set {Ref(_mortal)}=WIZARD");
		var thing = (await CmdAs(_mortal.DbRef, _mortal.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName("RoleGuideOwn")}")).Trim();
		await CmdAs(_mortal.DbRef, _mortal.Handle, $"@set {thing}=WIZARD");
		await Assert.That(await Eval($"hasrole({thing},wizard)")).IsEqualTo("1");
		await Cmd($"@set {Ref(_mortal)}=!WIZARD");
	}

	[Test]
	public async Task MisspelledNamesFailClosed()
	{
		await Assert.That(await Eval($"hasrole({Ref(_newbie)},aproved{_approved})")).IsEqualTo("0");
		await Assert.That(await Eval($"permission({Ref(_approver)},chargen.aprove)")).IsEqualTo("#-1 NO SUCH PERMISSION");
		await Assert.That(await Eval($"if(permission({Ref(_approver)},chargen.aprove),yes,no)")).IsEqualTo("no");
	}

	[Test]
	public async Task OverrideTakesThePermissionFromOnePerson()
	{
		await Cmd($"@permission/deny {Ref(_approver)}={_scope}");
		await Assert.That(await Eval($"permission({Ref(_approver)},{_scope})")).IsEqualTo("0");
		await Assert.That(await Eval($"hasrole({Ref(_approver)},{_approverRole})")).IsEqualTo("1");
		await Assert.That(await Heard(_approver, $"{_command} {_newbie.Name}")).Contains("Permission denied.");
		await Cmd($"@permission/clear {Ref(_approver)}={_scope}");
	}
}
