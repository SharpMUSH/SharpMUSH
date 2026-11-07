using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The everyday <c>player.*</c> permissions: every object holds them until a role or override denies one
/// (<c>help roles everyday</c>).
/// </summary>
public class PlayerScopeTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "EveryWiz");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "EveryMortal");
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

	private string Mortal => $"#{_mortal.DbRef.Number}";

	[Test]
	public async Task EveryoneHoldsThemUntilDenied()
	{
		await Assert.That(await Eval($"permission({Mortal},player.create)")).IsEqualTo("1");
		await Cmd($"@permission/deny {Mortal}=player.create");
		await Assert.That(await Eval($"permission({Mortal},player.create)")).IsEqualTo("0");
		await Assert.That(await As(_mortal, $"@create {TestIsolationHelpers.GenerateUniqueName("Denied")}")).StartsWith("#-1 PERMISSION DENIED");
		await Assert.That(await EvalAs(_mortal.DbRef, "create(Denied)")).StartsWith("#-1 PERMISSION DENIED");
		await Cmd($"@permission/clear {Mortal}=player.create");
		await Assert.That(await As(_mortal, $"@create {TestIsolationHelpers.GenerateUniqueName("Allowed")}")).StartsWith("#");
	}

	[Test]
	public async Task APageDenyWithAReachKeepsStaffOut()
	{
		await Assert.That(await Cmd($"@permission/deny {Mortal}=player.page/wizard")).Contains("reaching wizard");
		var before = Notifications.CountFor(_mortal.DbRef);
		await As(_mortal, $"page #{_wizard.DbRef.Number}=hello");
		await Assert.That(string.Join("\n", Notifications.For(_mortal.DbRef).Skip(before))).Contains("player.page");
		await Cmd($"@permission/clear {Mortal}=player.page");
	}

	[Test]
	public async Task DeniedDollarCommandsAreNotMatched()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("dollar");
		var thing = DBRef.Parse(await As(_mortal, $"@create {TestIsolationHelpers.GenerateUniqueName("Cmds")}")).Number;
		await As(_mortal, $"&CMD #{thing}=$everyday{marker}:&RAN %#={marker}");
		await Cmd($"@permission/deny {Mortal}=player.commands");
		await As(_mortal, $"everyday{marker}");
		await Assert.That(await Eval($"get({Mortal}/RAN)")).IsEmpty();
		await Assert.That(await As(_mortal, $"with #{thing}=everyday{marker}")).StartsWith("#-1 PERMISSION DENIED");
		await Cmd($"@permission/clear {Mortal}=player.commands");
	}

	[Test]
	public async Task ChatDenyCoversTheCemitFunction()
	{
		await Cmd($"@permission/deny {Mortal}=player.chat");
		await Assert.That(await EvalAs(_mortal.DbRef, "cemit(Public,hi)")).StartsWith("#-1 PERMISSION DENIED");
		await Cmd($"@permission/clear {Mortal}=player.chat");
	}

	[Test]
	public async Task PasswordAndForceAreDeniable()
	{
		await Cmd($"@permission/deny {Mortal}=player.password player.force");
		await Assert.That(await As(_mortal, "@password a=b")).StartsWith("#-1 PERMISSION DENIED");
		await Assert.That(await As(_mortal, "@force me=think hi")).StartsWith("#-1 PERMISSION DENIED");
		await Cmd($"@permission/clear {Mortal}=player.password player.force");
	}

	[Test]
	public async Task ADenyOnAPlayerHoldsForWhatItOwns()
	{
		var thing = DBRef.Parse(await As(_mortal, $"@create {TestIsolationHelpers.GenerateUniqueName("Owned")}"));
		await Assert.That(await EvalAs(thing, $"create({TestIsolationHelpers.GenerateUniqueName("ByThing")})")).StartsWith("#");
		await Cmd($"@permission/deny {Mortal}=player.create");
		await Assert.That(await EvalAs(thing, $"create({TestIsolationHelpers.GenerateUniqueName("ByThing")})")).StartsWith("#-1 PERMISSION DENIED");
		await Cmd($"@permission/clear {Mortal}=player.create");
	}

	[Test]
	public async Task AChatDenyStillLetsAPlayerStopListening()
	{
		await Cmd($"@permission/deny {Mortal}=player.chat");
		await Assert.That(await As(_mortal, "@channel/gag Public")).DoesNotStartWith("#-1 PERMISSION DENIED");
		await Assert.That(await As(_mortal, "@channel/on Public")).StartsWith("#-1 PERMISSION DENIED");
		await Cmd($"@permission/clear {Mortal}=player.chat");
	}
}
