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

	private Task<DBRef> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, namePrefix);

	// Validates the ArangoDB ownership edge is stored correctly:
	// FROM objects/{key} TO players/{key} (not players/{key} TO players/{key}).
	// See: ArangoDatabase.Objects.cs CreatePlayerAsync
	[Test]
	public async Task Player_SelfOwnership_OwnerEqualsPlayer()
	{
		var playerDbRef = await CreateTestPlayerAsync("SelfOwnership");

		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(playerNode.IsNone).IsFalse();

		var owner = await playerNode.Known.Object().Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);
	}

	[Test]
	public async Task GodPlayer_SelfOwnership_OwnerEqualsGod()
	{
		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		await Assert.That(godNode.IsNone).IsFalse();

		var owner = await godNode.Known.Object().Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(1);
	}

	[Test]
	public async Task Destroy_NonPlayerThing_FirstDestroy_MarksObjectAsGoing()
	{
		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_DestroyThing_NonPlayerTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@destroy {thingDbRef}"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		await Assert.That(obj.IsNone).IsFalse();
		var isGoing = await obj.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	[Test]
	public async Task Destroy_Player_RequiresNuke_NukeMarksPlayerAsGoing()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var playerDbRef = await CreateTestPlayerAsync("NukeRequired");

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@destroy {playerDbRef}"));

		await NotifyService
			.Received() // Weak check
			.NotifyAndReturn(
				executor,
				Arg.Is<string>(s => s.Contains("#-1 PERMISSION DENIED")),
				Arg.Is<string>(s => s.Contains("You must use @nuke to destroy a player.")),
				Arg.Any<bool>());

		var playerBeforeNuke = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var goingBeforeNuke = await playerBeforeNuke.Known.HasFlag("GOING");
		await Assert.That(goingBeforeNuke).IsFalse();

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		var playerAfterNuke = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var goingAfterNuke = await playerAfterNuke.Known.HasFlag("GOING");
		await Assert.That(goingAfterNuke).IsTrue();
	}

	[Test]
	public async Task Nuke_Player_MarksPlayerAsGoing()
	{
		var playerDbRef = await CreateTestPlayerAsync("MarksGoing");

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		var player = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(player.IsNone).IsFalse();
		var isGoing = await player.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	[Test]
	public async Task Nuke_Player_OwnedChannelTransfersToProbatePlayer()
	{
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

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		var channelAfter = await Mediator.Send(new GetChannelQuery("PDT_ChannelChown"));
		await Assert.That(channelAfter).IsNotNull();
		var ownerAfter = await channelAfter!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	[Test]
	public async Task Nuke_Player_NonSafePossession_IsMarkedAsGoing()
	{
		var playerDbRef = await CreateTestPlayerAsync("NonSafePossession");

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_NonSafeThing_PossessionTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@chown {thingDbRef}={playerDbRef}"));

		var thingBeforeNuke = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var ownerBefore = await thingBeforeNuke.Known.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);

		// nuke the player  (destroy_possessions=yes)
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		var thingAfterNuke = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var isGoing = await thingAfterNuke.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	[Test]
	public async Task Nuke_Player_SafePossession_IsChownedToProbateNotGoing()
	{
		var playerDbRef = await CreateTestPlayerAsync("SafePossession");

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_SafeThing_PossessionTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@set {thingDbRef}=SAFE"));

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@chown {thingDbRef}={playerDbRef}"));

		// nuke the player  (really_safe=yes → SAFE things survive)
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		var thingAfterNuke = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var isGoing = await thingAfterNuke.Known.HasFlag("GOING");
		await Assert.That(isGoing).IsFalse();

		var ownerAfter = await thingAfterNuke.Known.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	// Validates that the attribute re-assignment step (which runs AFTER channel-chown
	// and possession processing) still correctly reassigns all attributes owned by the
	// deleted player.
	[Test]
	public async Task Nuke_Player_AttributeOwnerReassignedToProbatePlayer()
	{
		var playerDbRef = await CreateTestPlayerAsync("AttrOwner");
		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var testPlayer = playerNode.Known.AsPlayer;

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_AttrOwnerThing_ReassignTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Set an attribute with the test player as attribute owner, simulating the test
		// player having authored an attribute on some object.
		var attrName = "PDT_ATTR_OWNER_TEST";
		await Database.SetAttributeAsync(
			thingDbRef,
			[attrName],
			A.single("PDT attribute value for owner reassign test"),
			testPlayer);

		var attrBefore = await Database.GetAttributeAsync(thingDbRef, [attrName]).LastOrDefaultAsync();
		await Assert.That(attrBefore).IsNotNull();
		var ownerBefore = await attrBefore!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore).IsNotNull();
		await Assert.That(ownerBefore!.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		var attrAfter = await Database.GetAttributeAsync(thingDbRef, [attrName]).LastOrDefaultAsync();
		await Assert.That(attrAfter).IsNotNull();
		var ownerAfter = await attrAfter!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter).IsNotNull();
		await Assert.That(ownerAfter!.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	// Combined: nuke a player who owns a channel, a non-SAFE thing, a SAFE thing, and
	// has attribute ownership; verify all three phases (channel-chown,
	// possession-processing, attr-reassign) are completed in a single @nuke call.
	[Test]
	public async Task Nuke_Player_CombinedScenario_AllPhasesComplete()
	{
		var playerDbRef = await CreateTestPlayerAsync("CombinedScenario");
		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var testPlayer = playerNode.Known.AsPlayer;

		await Mediator.Send(new CreateChannelCommand(
			MModule.single("PDT_CombinedChannel"),
			["Open"],
			testPlayer));

		var nonSafeResult = await Parser.CommandParse(
			1, ConnectionService,
			MModule.single("@create PDT_Combined_NonSafeThing"));
		var nonSafeDbRef = DBRef.Parse(nonSafeResult.Message!.ToPlainText()!);
		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@chown {nonSafeDbRef}={playerDbRef}"));

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

		var probateNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(ProbateJudgeDbRefNumber)));
		var attrName = "PDT_COMBINED_ATTR_TEST";
		await Database.SetAttributeAsync(
			probateNode.Known.Object().DBRef,
			[attrName],
			A.single("PDT combined test attribute value"),
			testPlayer);

		await Parser.CommandParse(
			1, ConnectionService,
			MModule.single($"@nuke {playerDbRef}"));

		var playerObj = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(await playerObj.Known.HasFlag("GOING")).IsTrue();

		var combinedChannel = await Mediator.Send(new GetChannelQuery("PDT_CombinedChannel"));
		await Assert.That(combinedChannel).IsNotNull();
		var channelOwner = await combinedChannel!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(channelOwner.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);

		var nonSafeObj = await Mediator.Send(new GetObjectNodeQuery(nonSafeDbRef));
		await Assert.That(await nonSafeObj.Known.HasFlag("GOING")).IsTrue();

		var safeObj = await Mediator.Send(new GetObjectNodeQuery(safeDbRef));
		await Assert.That(await safeObj.Known.HasFlag("GOING")).IsFalse();
		var safeOwner = await safeObj.Known.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(safeOwner.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);

		var attr = await Database.GetAttributeAsync(
			probateNode.Known.Object().DBRef,
			[attrName]).LastOrDefaultAsync();
		await Assert.That(attr).IsNotNull();
		var attrOwner = await attr!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(attrOwner).IsNotNull();
		await Assert.That(attrOwner!.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}
}
