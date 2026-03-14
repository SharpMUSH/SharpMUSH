using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Integration tests for the player-destruction process implemented in
/// <c>DestroyObjectAsync</c> / <c>HandlePlayerPossessionsAsync</c> (BuildingCommands.cs).
///
/// Test-config invariants (mushcnf.dst):
///   destroy_possessions = yes   → non-SAFE possessions are marked GOING
///   really_safe          = yes   → SAFE possessions are chowned to probate instead
///   probate_judge        = 1     → probate player is #1 (God / the test executor)
///
/// Each test creates fresh, uniquely-named objects so that shared-session state
/// from other test classes does not interfere.
/// </summary>
[NotInParallel]
public class PlayerDestructionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	// probate_judge = 1 in mushcnf.dst  →  God (#1) is the probate player.
	private const int ProbateJudgeDbRefNumber = 1;

	// -----------------------------------------------------------------------
	// Helper: create a fresh player via the database layer (avoids @pcreate
	// argument-parsing edge-cases) and return its DBRef.
	// -----------------------------------------------------------------------
	private async Task<DBRef> CreateTestPlayerAsync(string uniqueSuffix)
	{
		var options = WebAppFactoryArg.Services
			.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		var defaultHome = new DBRef((int)options.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)options.CurrentValue.Limit.StartingQuota;

		return await Mediator.Send(new CreatePlayerCommand(
			$"PDT_Player_{uniqueSuffix}",
			"TestPassword123",
			defaultHome,
			defaultHome,
			startingQuota));
	}

	// -----------------------------------------------------------------------
	// Test 1a – Players are always owned by themselves
	//           Validates the ArangoDB ownership edge is stored correctly:
	//           FROM objects/{key} TO players/{key} (not players/{key} TO players/{key}).
	//           See: ArangoDatabase.Objects.cs CreatePlayerAsync
	// -----------------------------------------------------------------------
	[Test]
	public async Task Player_SelfOwnership_OwnerEqualsPlayer()
	{
		// Arrange: create a fresh player
		var playerDbRef = await CreateTestPlayerAsync("SelfOwnership");

		// Act: query the player's owner through the standard DB path
		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(playerNode.IsNone).IsFalse();

		var owner = await playerNode.Known.Object().Owner.WithCancellation(CancellationToken.None);

		// Assert: the player owns themselves
		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);
	}

	// -----------------------------------------------------------------------
	// Test 1b – God player (#1) is also owned by themselves (migration data)
	// -----------------------------------------------------------------------
	[Test]
	public async Task GodPlayer_SelfOwnership_OwnerEqualsGod()
	{
		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		await Assert.That(godNode.IsNone).IsFalse();

		var owner = await godNode.Known.Object().Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(1);
	}

	// -----------------------------------------------------------------------
	// Test 3 – @destroy on a non-player (thing) marks it as GOING
	// -----------------------------------------------------------------------
	[Test]
	public async Task Destroy_NonPlayerThing_FirstDestroy_MarksObjectAsGoing()
	{
		// Arrange: create a uniquely-named thing
		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_DestroyThing_NonPlayerTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Act: first @destroy → should mark GOING
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@destroy {thingDbRef}"));

		// Assert: GOING flag is set
		var obj = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		await Assert.That(obj.IsNone).IsFalse();
		var isGoing = await obj.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	// -----------------------------------------------------------------------
	// Test 4 – @destroy on a player is rejected; @nuke succeeds
	// -----------------------------------------------------------------------
	[Test]
	public async Task Destroy_Player_RequiresNuke_NukeMarksPlayerAsGoing()
	{
		// Arrange
		var playerDbRef = await CreateTestPlayerAsync("NukeRequired");

		// Act – plain @destroy should be rejected (player requires @nuke)
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@destroy {playerDbRef}"));

		// The NotifyService mock should have received the "must use @nuke" notification
		await NotifyService
			.Received()
			.NotifyAndReturn(
				Arg.Any<DBRef>(),
				Arg.Is<string>(s => s.Contains("#-1")),
				Arg.Is<string>(s => s.Contains("nuke")),
				Arg.Any<bool>());

		// Player should NOT be GOING yet
		var playerBeforeNuke = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var goingBeforeNuke = await playerBeforeNuke.Known.HasFlag("GOING");
		await Assert.That(goingBeforeNuke).IsFalse();

		// Act – @nuke should succeed (executor #1 is Wizard)
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		// Assert: player is now GOING
		var playerAfterNuke = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var goingAfterNuke = await playerAfterNuke.Known.HasFlag("GOING");
		await Assert.That(goingAfterNuke).IsTrue();
	}

	// -----------------------------------------------------------------------
	// Test 5 – @nuke marks the player itself as GOING
	// -----------------------------------------------------------------------
	[Test]
	public async Task Nuke_Player_MarksPlayerAsGoing()
	{
		// Arrange
		var playerDbRef = await CreateTestPlayerAsync("MarksGoing");

		// Act
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		// Assert
		var player = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(player.IsNone).IsFalse();
		var isGoing = await player.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	// -----------------------------------------------------------------------
	// Test 6 – @nuke transfers channel ownership to the probate player (#1)
	// -----------------------------------------------------------------------
	[Test]
	public async Task Nuke_Player_OwnedChannelTransfersToProbatePlayer()
	{
		// Arrange: create a player and a channel owned by that player
		var playerDbRef = await CreateTestPlayerAsync("ChannelChown");
		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var testPlayer = playerNode.Known.AsPlayer;

		await Mediator.Send(new CreateChannelCommand(
			MModule.single("PDT_ChannelChown"),
			["Open"],
			testPlayer));

		var channelBefore = await Mediator.Send(new GetChannelQuery("PDT_ChannelChown"));
		await Assert.That(channelBefore).IsNotNull();
		var ownerBefore = await channelBefore!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);

		// Act
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		// Assert: channel is now owned by probate player (#1)
		var channelAfter = await Mediator.Send(new GetChannelQuery("PDT_ChannelChown"));
		await Assert.That(channelAfter).IsNotNull();
		var ownerAfter = await channelAfter!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	// -----------------------------------------------------------------------
	// Test 7 – @nuke marks non-SAFE possessions as GOING
	//          (destroy_possessions=yes, really_safe=yes in test config)
	// -----------------------------------------------------------------------
	[Test]
	public async Task Nuke_Player_NonSafePossession_IsMarkedAsGoing()
	{
		// Arrange: create a player, create a thing, chown it to that player
		var playerDbRef = await CreateTestPlayerAsync("NonSafePossession");

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_NonSafeThing_PossessionTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Chown the thing to the test player
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@chown {thingDbRef}={playerDbRef}"));

		// Verify ownership was transferred
		var thingBeforeNuke = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var ownerBefore = await thingBeforeNuke.Known.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);

		// Act: nuke the player  (destroy_possessions=yes)
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		// Assert: the non-SAFE thing is now marked GOING
		var thingAfterNuke = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var isGoing = await thingAfterNuke.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	// -----------------------------------------------------------------------
	// Test 8 – @nuke chowns SAFE possessions to probate, not marks them GOING
	//          (really_safe=yes in test config)
	// -----------------------------------------------------------------------
	[Test]
	public async Task Nuke_Player_SafePossession_IsChownedToProbateNotGoing()
	{
		// Arrange: create a player, create a SAFE thing, chown it to that player
		var playerDbRef = await CreateTestPlayerAsync("SafePossession");

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_SafeThing_PossessionTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Mark the thing as SAFE
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@set {thingDbRef}=SAFE"));

		// Chown the thing to the test player
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@chown {thingDbRef}={playerDbRef}"));

		// Act: nuke the player  (really_safe=yes → SAFE things survive)
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		// Assert: SAFE thing is NOT going, and its owner is probate (#1)
		var thingAfterNuke = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var isGoing = await thingAfterNuke.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsFalse();

		var ownerAfter = await thingAfterNuke.Known.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	// -----------------------------------------------------------------------
	// Test 9 – @nuke reassigns attribute ownership to the probate player
	//          This validates that the attribute re-assignment step (which runs
	//          AFTER channel-chown and possession processing per the PR) still
	//          correctly reassigns all attributes owned by the deleted player.
	// -----------------------------------------------------------------------
	[Test]
	public async Task Nuke_Player_AttributeOwnerReassignedToProbatePlayer()
	{
		// Arrange: create a player
		var playerDbRef = await CreateTestPlayerAsync("AttrOwner");
		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var testPlayer = playerNode.Known.AsPlayer;

		// Create an object owned by executor #1
		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_AttrOwnerThing_ReassignTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Set an attribute directly on the object, with the test player as attribute owner.
		// This simulates the test player having authored an attribute on some object.
		var attrName = "PDT_ATTR_OWNER_TEST";
		await Database.SetAttributeAsync(
			thingDbRef,
			[attrName],
			A.single("PDT attribute value for owner reassign test"),
			testPlayer);

		// Verify the attribute owner is the test player before the nuke
		var attrBefore = await Database.GetAttributeAsync(thingDbRef, [attrName]).LastOrDefaultAsync();
		await Assert.That(attrBefore).IsNotNull();
		var ownerBefore = await attrBefore!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore).IsNotNull();
		await Assert.That(ownerBefore!.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);

		// Act: nuke the player.  HandlePlayerPossessionsAsync runs:
		//   1. channel-chown
		//   2. possession processing (thing is owned by #1, not the player — so no change)
		//   3. ReassignAttributeOwnerCommand  ← runs last, per the PR
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		// Assert: attribute owner is now the probate player (#1)
		var attrAfter = await Database.GetAttributeAsync(thingDbRef, [attrName]).LastOrDefaultAsync();
		await Assert.That(attrAfter).IsNotNull();
		var ownerAfter = await attrAfter!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter).IsNotNull();
		await Assert.That(ownerAfter!.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	// -----------------------------------------------------------------------
	// Test 10 – Combined: nuke a player who owns a channel, a non-SAFE thing,
	//          a SAFE thing, and has attribute ownership; verify all three
	//          phases (channel-chown, possession-processing, attr-reassign)
	//          are completed in a single @nuke call.
	// -----------------------------------------------------------------------
	[Test]
	public async Task Nuke_Player_CombinedScenario_AllPhasesComplete()
	{
		// Arrange
		var playerDbRef = await CreateTestPlayerAsync("CombinedScenario");
		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var testPlayer = playerNode.Known.AsPlayer;

		// Channel owned by test player
		await Mediator.Send(new CreateChannelCommand(
			MModule.single("PDT_CombinedChannel"),
			["Open"],
			testPlayer));

		// Non-SAFE thing chowned to test player
		var nonSafeResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_Combined_NonSafeThing"));
		var nonSafeDbRef = DBRef.Parse(nonSafeResult.Message!.ToPlainText()!);
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@chown {nonSafeDbRef}={playerDbRef}"));

		// SAFE thing chowned to test player
		var safeResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_Combined_SafeThing"));
		var safeDbRef = DBRef.Parse(safeResult.Message!.ToPlainText()!);
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@set {safeDbRef}=SAFE"));
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@chown {safeDbRef}={playerDbRef}"));

		// Attribute owned by test player on the executor's own object (#1)
		var probateNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(ProbateJudgeDbRefNumber)));
		var attrName = "PDT_COMBINED_ATTR_TEST";
		await Database.SetAttributeAsync(
			probateNode.Known.Object().DBRef,
			[attrName],
			A.single("PDT combined test attribute value"),
			testPlayer);

		// Act: single @nuke covering all three phases
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		// Assert – player is GOING
		var playerObj = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(await playerObj.Known.HasFlag("GOING")).IsTrue();

		// Assert – channel ownership → probate (#1)
		var combinedChannel = await Mediator.Send(new GetChannelQuery("PDT_CombinedChannel"));
		await Assert.That(combinedChannel).IsNotNull();
		var channelOwner = await combinedChannel!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(channelOwner.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);

		// Assert – non-SAFE thing → GOING
		var nonSafeObj = await Mediator.Send(new GetObjectNodeQuery(nonSafeDbRef));
		await Assert.That(await nonSafeObj.Known.HasFlag("GOING")).IsTrue();

		// Assert – SAFE thing → chowned to probate, not GOING
		var safeObj = await Mediator.Send(new GetObjectNodeQuery(safeDbRef));
		await Assert.That(await safeObj.Known.HasFlag("GOING")).IsFalse();
		var safeOwner = await safeObj.Known.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(safeOwner.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);

		// Assert – attribute ownership → probate (#1)
		var attr = await Database.GetAttributeAsync(
			probateNode.Known.Object().DBRef,
			[attrName]).LastOrDefaultAsync();
		await Assert.That(attr).IsNotNull();
		var attrOwner = await attr!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(attrOwner).IsNotNull();
		await Assert.That(attrOwner!.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}
}
