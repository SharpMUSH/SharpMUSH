using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Which objects become GOING together, and which recover together — PennMUSH
/// <c>pre_destroy()</c> and <c>undestroy()</c> (<c>src/destroy.c:469-610</c>), whose header comment
/// explains the "two votes" compromise the last three tests pin.
/// <list type="bullet">
///   <item>Scheduling a room schedules its exits; scheduling a player schedules what they would lose.</item>
///   <item>Sparing anything spares its GOING owner; sparing an exit spares its source room.</item>
///   <item>Sparing a room spares its exits, except those a GOING player's destruction still dooms.</item>
///   <item>Sparing a player spares their things, except exits in a GOING room someone else owns.</item>
/// </list>
/// Test config: destroy_possessions and really_safe on.
/// </summary>
[NotInParallel]
public class DestroyCascadeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private ValueTask<CallState> AsGod(string command) =>
		Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private static DBRef Parse(CallState result) => DBRef.Parse(result.Message!.ToPlainText().Trim());

	private async Task<DBRef> CreateAsync(string prefix)
		=> Parse(await AsGod($"@create {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

	private async Task<DBRef> DigAsync(string prefix)
		=> Parse(await AsGod($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

	private async Task<DBRef> OpenAsync(string prefix, DBRef source, DBRef destination)
		=> Parse(await AsGod($"@open {TestIsolationHelpers.GenerateUniqueName(prefix)}={destination},{source}"));

	private Task<DBRef> PlayerAsync(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, prefix);

	private async Task<bool> IsGoingAsync(DBRef target)
		=> await (await Mediator.Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>().HasFlag("GOING");

	[Test]
	public async Task Room_SchedulesItsExits_AndSparingItSparesThem()
	{
		var elsewhere = await DigAsync("DCT_Elsewhere");
		var room = await DigAsync("DCT_Room");
		var first = await OpenAsync("DCT_RoomExitA", room, elsewhere);
		var second = await OpenAsync("DCT_RoomExitB", room, elsewhere);

		await AsGod($"@destroy {room}");

		await Assert.That(await IsGoingAsync(first)).IsTrue();
		await Assert.That(await IsGoingAsync(second)).IsTrue();
		await Assert.That(await IsGoingAsync(elsewhere)).IsFalse();

		await AsGod($"@undestroy {room}");

		await Assert.That(await IsGoingAsync(room)).IsFalse();
		await Assert.That(await IsGoingAsync(first)).IsFalse();
		await Assert.That(await IsGoingAsync(second)).IsFalse();
	}

	[Test]
	public async Task SparingAnExit_SparesItsSourceRoom_AndTheRoomsOtherExits()
	{
		var elsewhere = await DigAsync("DCT_ExitElsewhere");
		var room = await DigAsync("DCT_ExitRoom");
		var spared = await OpenAsync("DCT_Spared", room, elsewhere);
		var sibling = await OpenAsync("DCT_Sibling", room, elsewhere);

		await AsGod($"@destroy {room}");
		await AsGod($"@undestroy {spared}");

		await Assert.That(await IsGoingAsync(spared)).IsFalse();
		await Assert.That(await IsGoingAsync(room)).IsFalse()
			.Because("an exit cannot outlive the room it leads from");
		await Assert.That(await IsGoingAsync(sibling)).IsFalse()
			.Because("sparing the room spares its exits");
	}

	[Test]
	public async Task Player_SchedulesTheirPossessions_AndSparingThemSparesAll()
	{
		var player = await PlayerAsync("DCT_Owner");
		var thing = await CreateAsync("DCT_OwnedThing");
		var room = await DigAsync("DCT_OwnedRoom");
		var elsewhere = await DigAsync("DCT_OwnedElsewhere");
		var exit = await OpenAsync("DCT_OwnedRoomExit", room, elsewhere);
		var safe = await CreateAsync("DCT_OwnedSafe");
		await AsGod($"@set {safe}=SAFE");
		foreach (var owned in new[] { thing, room, exit, safe })
		{
			await AsGod($"@chown {owned}={player}");
		}

		await AsGod($"@nuke {player}");

		await Assert.That(await IsGoingAsync(thing)).IsTrue();
		await Assert.That(await IsGoingAsync(room)).IsTrue();
		await Assert.That(await IsGoingAsync(exit)).IsTrue();
		await Assert.That(await IsGoingAsync(safe)).IsFalse();

		await AsGod($"@undestroy {player}");

		await Assert.That(await IsGoingAsync(player)).IsFalse();
		await Assert.That(await IsGoingAsync(thing)).IsFalse();
		await Assert.That(await IsGoingAsync(room)).IsFalse();
		await Assert.That(await IsGoingAsync(exit)).IsFalse();
	}

	[Test]
	public async Task SparingAPossession_SparesItsGoingOwner_AndTheirOtherPossessions()
	{
		var player = await PlayerAsync("DCT_Rescued");
		var spared = await CreateAsync("DCT_RescuedThing");
		var sibling = await CreateAsync("DCT_RescuedSibling");
		await AsGod($"@chown {spared}={player}");
		await AsGod($"@chown {sibling}={player}");

		await AsGod($"@nuke {player}");
		await AsGod($"@undestroy {spared}");

		await Assert.That(await IsGoingAsync(spared)).IsFalse();
		await Assert.That(await IsGoingAsync(player)).IsFalse()
			.Because("nothing may survive its owner's destruction");
		await Assert.That(await IsGoingAsync(sibling)).IsFalse();
	}

	/// <summary>
	/// Penn's worked example: A owns room #1, B owns exit #2 in it and thing #3; both players are
	/// scheduled. Sparing #3 spares B, but #2 keeps its other "vote" — the GOING room — and so do A
	/// and the room.
	/// </summary>
	[Test]
	public async Task SparingAPlayer_LeavesTheirExitInSomeoneElsesGoingRoom()
	{
		var a = await PlayerAsync("DCT_VoteA");
		var b = await PlayerAsync("DCT_VoteB");
		var room = await DigAsync("DCT_VoteRoom");
		var elsewhere = await DigAsync("DCT_VoteElsewhere");
		var exit = await OpenAsync("DCT_VoteExit", room, elsewhere);
		var thing = await CreateAsync("DCT_VoteThing");
		await AsGod($"@chown {room}={a}");
		await AsGod($"@chown {exit}={b}");
		await AsGod($"@chown {thing}={b}");

		await AsGod($"@nuke {a}");
		await AsGod($"@nuke {b}");
		await AsGod($"@undestroy {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsFalse();
		await Assert.That(await IsGoingAsync(b)).IsFalse();
		await Assert.That(await IsGoingAsync(exit)).IsTrue();
		await Assert.That(await IsGoingAsync(room)).IsTrue();
		await Assert.That(await IsGoingAsync(a)).IsTrue();
	}

	[Test]
	public async Task SparingARoom_LeavesAnExitAGoingPlayerStillDooms()
	{
		var b = await PlayerAsync("DCT_ExitOwner");
		var room = await DigAsync("DCT_KeptRoom");
		var elsewhere = await DigAsync("DCT_KeptElsewhere");
		var doomed = await OpenAsync("DCT_DoomedExit", room, elsewhere);
		var ours = await OpenAsync("DCT_OurExit", room, elsewhere);
		await AsGod($"@chown {doomed}={b}");

		await AsGod($"@nuke {b}");
		await AsGod($"@nuke {room}");
		await AsGod($"@undestroy {room}");

		await Assert.That(await IsGoingAsync(room)).IsFalse();
		await Assert.That(await IsGoingAsync(ours)).IsFalse();
		await Assert.That(await IsGoingAsync(doomed)).IsTrue()
			.Because("its owner's purge still frees it");
		await Assert.That(await IsGoingAsync(b)).IsTrue();
	}

	/// <summary>
	/// A SAFE exit survives its owner's purge, so the room's schedule was its only one and sparing the
	/// room spares it — and, since nothing may outlive a GOING owner, the owner too, which in turn
	/// spares the owner's other exit now that its room is no longer GOING.
	/// </summary>
	[Test]
	public async Task SparingARoom_SparesASafeExit_AndThroughItTheOwner()
	{
		var b = await PlayerAsync("DCT_SafeExitOwner");
		var room = await DigAsync("DCT_SafeRoom");
		var elsewhere = await DigAsync("DCT_SafeElsewhere");
		var safe = await OpenAsync("DCT_SafeExit", room, elsewhere);
		var other = await OpenAsync("DCT_OtherExit", room, elsewhere);
		await AsGod($"@set {safe}=SAFE");
		await AsGod($"@chown {safe}={b}");
		await AsGod($"@chown {other}={b}");

		await AsGod($"@nuke {b}");
		await AsGod($"@nuke {room}");
		await AsGod($"@undestroy {room}");

		await Assert.That(await IsGoingAsync(room)).IsFalse();
		await Assert.That(await IsGoingAsync(safe)).IsFalse();
		await Assert.That(await IsGoingAsync(b)).IsFalse();
		await Assert.That(await IsGoingAsync(other)).IsFalse();
	}
}
