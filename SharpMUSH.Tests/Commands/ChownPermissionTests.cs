using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Who may <c>@chown</c> what to whom, as PennMUSH's <c>do_chown</c> and <c>chown_ok</c> decide it
/// (<c>src/set.c:177-290</c>). SharpMUSH asked only whether the enactor controlled both the object and
/// the new owner, so CHOWN_OK did nothing and a player could not give a room to a Zone Master.
/// </summary>
public class ChownPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task AsGod(string command) =>
		CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix) =>
		await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, prefix);

	/// <summary>Runs <paramref name="command"/> as <paramref name="player"/> and returns what they were told.</summary>
	private async Task<List<string>> RunAs(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
	}

	/// <summary>A room <paramref name="owner"/> owns, dug by God.</summary>
	private async Task<string> RoomOwnedBy(TestIsolationHelpers.TestPlayer owner, string prefix)
	{
		var room = await Eval($"dig({TestIsolationHelpers.GenerateUniqueName(prefix)})");
		await AsGod($"@chown {room}={owner.DbRef}");
		return room;
	}

	[Test]
	public async ValueTask AChownOkRoomCanBeTakenByAnyone()
	{
		var owner = await PlayerAsync("ChownOkGiver");
		var taker = await PlayerAsync("ChownOkTaker");
		var room = await RoomOwnedBy(owner, "ChownOkRoom");
		await RunAs(owner, $"@set {room}=CHOWN_OK");

		await Assert.That(await RunAs(taker, $"@chown {room}=me")).Contains("Owner changed.");
		await Assert.That(await Eval($"owner({room})")).IsEqualTo($"#{taker.DbRef.Number}");
	}

	[Test]
	public async ValueTask ARoomWithoutChownOkStaysWithItsOwner()
	{
		var owner = await PlayerAsync("NoChownOkGiver");
		var taker = await PlayerAsync("NoChownOkTaker");
		var room = await RoomOwnedBy(owner, "NoChownOkRoom");

		await Assert.That(await RunAs(taker, $"@chown {room}=me"))
			.Contains($"Permission denied. You do not control {await Eval($"name({room})")}, and it is not CHOWN_OK.");
		await Assert.That(await Eval($"owner({room})")).IsEqualTo($"#{owner.DbRef.Number}");
	}

	/// <summary><c>chown_ok</c>'s third case asks the object's <c>@lock/chown</c> as well as its flag.</summary>
	[Test]
	public async ValueTask TheChownLockChoosesWhoMayTakeIt()
	{
		var owner = await PlayerAsync("ChownLockGiver");
		var chosen = await PlayerAsync("ChownLockChosen");
		var other = await PlayerAsync("ChownLockOther");
		var room = await RoomOwnedBy(owner, "ChownLockRoom");
		await RunAs(owner, $"@set {room}=CHOWN_OK");
		await RunAs(owner, $"@lock/chown {room}=#{chosen.DbRef.Number}");

		await Assert.That(await RunAs(other, $"@chown {room}=me"))
			.Contains($"Permission denied. You do not pass the ChOwn lock on {await Eval($"name({room})")}.");
		await Assert.That(await RunAs(chosen, $"@chown {room}=me")).Contains("Owner changed.");
	}

	/// <summary>A CHOWN_OK thing is taken from the hands, not from across the room.</summary>
	[Test]
	public async ValueTask AChownOkThingHasToBeCarried()
	{
		var owner = await PlayerAsync("ChownThingGiver");
		var taker = await PlayerAsync("ChownThingTaker");
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("ChownThing")})");
		await AsGod($"@chown {thing}={owner.DbRef}");
		await AsGod($"@set {thing}=CHOWN_OK");

		await Assert.That(await RunAs(taker, $"@chown {thing}=me"))
			.Contains($"Permission denied. You must carry {await Eval($"name({thing})")} to @chown it.");

		await AsGod($"@teleport {thing}={taker.DbRef}");
		await Assert.That(await RunAs(taker, $"@chown {thing}=me")).Contains("Owner changed.");
	}

	/// <summary>
	/// A player may give what they own to a Zone Master (a SHARED player) whose zone lock they pass,
	/// named from anywhere; anyone else is refused, and so is giving to an ordinary player.
	/// </summary>
	[Test]
	public async ValueTask APlayerGivesARoomToAZoneMasterTheyPass()
	{
		var builder = await PlayerAsync("ZmpBuilder");
		var stranger = await PlayerAsync("ZmpStranger");
		var zoneMaster = await PlayerAsync("ZmpMaster");
		await AsGod($"@set {zoneMaster.DbRef}=SHARED");
		await AsGod($"@lock/zone {zoneMaster.DbRef}=#{builder.DbRef.Number}");
		var room = await RoomOwnedBy(builder, "ZmpRoom");

		await Assert.That(await RunAs(builder, $"@chown {room}={stranger.Name}"))
			.Contains($"Permission denied. You do not control {stranger.Name}.");
		await Assert.That(await RunAs(builder, $"@chown {room}={zoneMaster.Name}")).Contains("Owner changed.");
		await Assert.That(await Eval($"owner({room})")).IsEqualTo($"#{zoneMaster.DbRef.Number}");

		var strangersRoom = await RoomOwnedBy(stranger, "ZmpStrangerRoom");
		await Assert.That(await RunAs(stranger, $"@chown {strangersRoom}={zoneMaster.Name}"))
			.Contains($"Permission denied. You do not control {zoneMaster.Name}.");
	}

	[Test]
	public async ValueTask PlayersAlwaysOwnThemselves()
	{
		var player = await PlayerAsync("ChownSelf");
		var other = await PlayerAsync("ChownSelfOther");

		await Assert.That(await RunAs(player, $"@chown me={other.Name}")).Contains("Players always own themselves.");
	}

	[Test]
	public async ValueTask PreserveIsForWizards()
	{
		var player = await PlayerAsync("ChownPreserveMortal");
		var room = await RoomOwnedBy(player, "ChownPreserveRoom");

		await Assert.That(await RunAs(player, $"@chown/preserve {room}=me"))
			.Contains("You cannot @CHOWN/PRESERVE. Use normal @CHOWN.");
	}

	/// <summary>
	/// <c>do_chown</c> asks <c>can_pay_fees</c> of the new owner (<c>src/set.c:226</c>), which tells
	/// them their quota has run out; the object stays where it was.
	/// </summary>
	[Test]
	public async ValueTask TheNewOwnerNeedsAQuotaSlot()
	{
		var owner = await PlayerAsync("ChownQuotaGiver");
		var taker = await PlayerAsync("ChownQuotaTaker");
		var room = await RoomOwnedBy(owner, "ChownQuotaRoom");
		await RunAs(owner, $"@set {room}=CHOWN_OK");
		await AsGod($"@quota/set {taker.DbRef}=0");

		await Assert.That(await RunAs(taker, $"@chown {room}=me")).Contains("Sorry, your building quota has run out.");
		await Assert.That(await Eval($"owner({room})")).IsEqualTo($"#{owner.DbRef.Number}");
	}
}
