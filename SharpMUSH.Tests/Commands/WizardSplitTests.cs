using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The parts WIZARD is split into (<c>help roles</c>): a custom role allowing one of them lets its holder
/// do that part of a wizard's work, and nothing else, without being a wizard.
/// </summary>
public class WizardSplitTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _staff = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;
	private string _role = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "SplitWiz");
		_staff = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "SplitStaff");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "SplitMortal");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
		_role = "r" + Guid.NewGuid().ToString("N")[..12];
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await Heard(_wizard, $"@role/delete {_role}");
		foreach (var player in new[] { _wizard, _staff, _mortal })
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	/// <summary>Runs a command and returns what the executor was told, one line per notification.</summary>
	private async Task<string> Heard(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await CmdAs(who.DbRef, who.Handle, command);
		return string.Join("\n", WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before));
	}

	/// <summary>Gives the staff player a role of its own that allows <paramref name="scope"/>.</summary>
	private async Task Allow(string scope)
	{
		await Heard(_wizard, $"@role/create {_role}=Staff");
		await Heard(_wizard, $"@role/allow {_role}={scope}");
		await Heard(_wizard, $"@role/assign {Ref(_staff)}={_role}");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission({Ref(_staff)},{scope})")).IsEqualTo("1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasflag({Ref(_staff)},WIZARD)")).IsEqualTo("0");
	}

	private async Task<bool> IsMember(string channelName, DBRef who)
	{
		var channel = await Mediator.Send(new GetChannelQuery(channelName));
		return channel is not null
					 && await channel.Members.Value.AnyAsync(x => x.Member.Object().DBRef.Number == who.Number);
	}

	[Test]
	public async Task ChatAdminRunsWizardChannelsAndWalls()
	{
		await Allow("chat.admin");
		var channel = $"Wz{TestIsolationHelpers.GenerateUniqueName("c").Replace("_", string.Empty)[^8..]}";
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(channel), ["Player", "Wizard", "Open"], god));

		var joined = await Heard(_staff, $"@channel/on {channel}");
		await Heard(_mortal, $"@channel/on {channel}");
		await Assert.That(await IsMember(channel, _staff.DbRef)).IsTrue().Because(joined);
		await Assert.That(await IsMember(channel, _mortal.DbRef)).IsFalse();

		await Assert.That(await Heard(_staff, "@wizwall/emit split check")).DoesNotContain("Permission denied.");
		await Assert.That(await Heard(_mortal, "@wizwall/emit split check")).Contains("Permission denied.");
		await Assert.That(await EvalAs(_staff.DbRef, "wizmotd()")).DoesNotStartWith("#-1");
		await Assert.That(await EvalAs(_mortal.DbRef, "wizmotd()")).StartsWith("#-1");

		// chat.admin is not the rest of a wizard's work.
		await Assert.That(await Heard(_staff, "@storage")).Contains("Permission denied.");
	}

	[Test]
	public async Task PlayersModerateResetsPasswordsButNotAWizards()
	{
		await Allow("players.moderate");

		await Assert.That(await Heard(_staff, $"@newpassword {Ref(_mortal)}=SplitFresh1")).DoesNotContain("Permission denied.");
		await Assert.That(await EvalAs(new DBRef(1), $"checkpass({Ref(_mortal)},SplitFresh1)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_staff.DbRef, $"checkpass({Ref(_mortal)},SplitFresh1)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_mortal.DbRef, $"checkpass({Ref(_mortal)},SplitFresh1)")).StartsWith("#-1");

		await Assert.That(await Heard(_staff, $"@newpassword {Ref(_wizard)}=SplitTaken1")).Contains("Permission denied.");
		await Assert.That(await EvalAs(new DBRef(1), $"checkpass({Ref(_wizard)},SplitTaken1)")).IsEqualTo("0");
		await Assert.That(await Heard(_staff, "@newpassword #1=SplitTaken1")).Contains("Permission denied.");
		await Assert.That(await Heard(_wizard, "@newpassword #1=SplitTaken1")).Contains("Permission denied.");
	}

	[Test]
	public async Task ServerOperateRunsTheServerCommands()
	{
		await Allow("server.operate");

		await Assert.That(await Heard(_staff, "@storage")).DoesNotContain("Permission denied.");
		await Assert.That(await Heard(_mortal, "@storage")).Contains("Permission denied.");
		await Assert.That(await Heard(_staff, "@wizwall/emit split check")).Contains("Permission denied.");
	}

	[Test]
	public async Task WizardHoldsEveryPartAndRoyaltyOnlyViewsPlayers()
	{
		foreach (var scope in new[] { "chat.admin", "server.operate", "players.moderate", "config.admin", "packages.admin" })
			await Assert.That(await EvalAs(_wizard.DbRef, $"permission(me,{scope})")).IsEqualTo("1").Because(scope);

		await Cmd($"@set {Ref(_mortal)}=ROYALTY");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission({Ref(_mortal)},players.view)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission({Ref(_mortal)},players.moderate)")).IsEqualTo("0");
		await Assert.That(await EvalAs(_wizard.DbRef, $"permission({Ref(_mortal)},chat.admin)")).IsEqualTo("0");
		await Cmd($"@set {Ref(_mortal)}=!ROYALTY");
	}
}
