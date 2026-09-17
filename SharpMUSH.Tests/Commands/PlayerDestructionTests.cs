using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Integration tests for player destruction: <c>@nuke</c> only schedules (PennMUSH
/// <c>pre_destroy</c>), and probate happens when the player is actually freed (<c>clear_player</c>,
/// reached here through a second <c>@nuke</c>).
///
/// Test-config invariants (mushcnf.dst):
///   destroy_possessions = yes   → non-SAFE possessions are marked GOING, and freed with the player
///   really_safe          = yes   → SAFE possessions are left unmarked, and go to probate with the player
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

	// FROM objects/{key} TO players/{key} (not players/{key} TO players/{key}).
	[Test]
	public async Task Player_SelfOwnership_OwnerEqualsPlayer()
	{
		var playerDbRef = await CreateTestPlayerAsync("SelfOwnership");

		var playerNode = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<AnySharpObject>();

		var owner = await playerNode.Object().Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);
	}

	[Test]
	public async Task GodPlayer_SelfOwnership_OwnerEqualsGod()
	{
		var godNode = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();

		var owner = await godNode.Object().Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(1);
	}

	[Test]
	public async Task Destroy_NonPlayerThing_FirstDestroy_MarksObjectAsGoing()
	{
		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain("@create PDT_DestroyThing_NonPlayerTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@destroy {thingDbRef}"));

		var obj = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Expect<AnySharpObject>();
		var isGoing = await obj.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	[Test]
	public async Task Destroy_Player_RequiresNuke_NukeMarksPlayerAsGoing()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var playerDbRef = await CreateTestPlayerAsync("NukeRequired");

		await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@destroy {playerDbRef}"));

		await NotifyService
			.Received() // Weak check
			.NotifyAndReturn(
				executor,
				Arg.Is<string>(s => s.Contains("#-1 PERMISSION DENIED")),
				// what_to_destroy's ownership check precedes its player case, and a wizard does not
				// own another player, so this is the message Penn gives — not "use @nuke on a player".
				Arg.Is<string>(s => s.Contains("That object does not belong to you. Use @nuke to destroy it.")),
				Arg.Any<bool>());

		var playerBeforeNuke = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<AnySharpObject>();
		var goingBeforeNuke = await playerBeforeNuke.HasFlag("GOING");
		await Assert.That(goingBeforeNuke).IsFalse();

		await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@nuke {playerDbRef}"));

		var playerAfterNuke = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<AnySharpObject>();
		var goingAfterNuke = await playerAfterNuke.HasFlag("GOING");
		await Assert.That(goingAfterNuke).IsTrue();
	}

	[Test]
	public async Task Nuke_Player_MarksPlayerAsGoing()
	{
		var playerDbRef = await CreateTestPlayerAsync("MarksGoing");

		await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@nuke {playerDbRef}"));

		var player = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<AnySharpObject>();
		var isGoing = await player.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	[Test]
	public async Task Nuke_Player_OwnedChannelTransfersToProbatePlayer_OnlyWhenFreed()
	{
		var playerDbRef = await CreateTestPlayerAsync("ChannelChown");
		var testPlayer = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<SharpPlayer>();
		var channelName = TestIsolationHelpers.GenerateUniqueName("PDT_Chan");

		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(channelName), ["Open"], testPlayer));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));

		await Assert.That(await ChannelOwnerAsync(channelName)).IsEqualTo(playerDbRef.Number)
			.Because("scheduling is reversible; the channel changes hands only when the player is freed");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));

		await Assert.That(await ChannelOwnerAsync(channelName)).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	private async Task<int> ChannelOwnerAsync(string channelName)
	{
		var channel = await Mediator.Send(new GetChannelQuery(channelName));
		await Assert.That(channel).IsNotNull();
		return (await channel!.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number;
	}

	private async Task<int> OwnerOfAsync(DBRef target)
		=> (await (await Mediator.Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>()
			.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number;

	private async Task<int?> AttributeOwnerAsync(DBRef holder, string attrName)
	{
		var attr = await Database.GetAttributeAsync(holder, [attrName]).LastOrDefaultAsync();
		return attr is null ? null : (await attr.Owner.WithCancellation(CancellationToken.None))?.Object.DBRef.Number;
	}

	/// <summary>
	/// PennMUSH probates in <c>clear_player</c>, after the grace period, so cancelling a deletion gives
	/// nothing away. Every owner — channel, surviving and doomed possessions, authored attributes —
	/// is exactly what it was before the <c>@nuke</c>.
	/// </summary>
	[Test]
	public async Task Nuke_ThenUndestroy_Player_LeavesEveryOwnerUnchanged()
	{
		var playerDbRef = await CreateTestPlayerAsync("UndestroyOwners");
		var testPlayer = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<SharpPlayer>();
		var channelName = TestIsolationHelpers.GenerateUniqueName("PDT_UndChan");
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(channelName), ["Open"], testPlayer));

		var doomed = DBRef.Parse((await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("PDT_UndDoomed")}"))).Message!.ToPlainText());
		var safe = DBRef.Parse((await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("PDT_UndSafe")}"))).Message!.ToPlainText());
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {safe}=SAFE"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {doomed}={playerDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {safe}={playerDbRef}"));

		var attrName = $"PDT_UND_{Guid.NewGuid():N}";
		await Database.SetAttributeAsync(new DBRef(ProbateJudgeDbRefNumber), [attrName], MarkupText.Plain("authored"), testPlayer);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@undestroy {playerDbRef}"));

		try
		{
			await Assert.That(await ChannelOwnerAsync(channelName)).IsEqualTo(playerDbRef.Number);
			await Assert.That(await OwnerOfAsync(doomed)).IsEqualTo(playerDbRef.Number);
			await Assert.That(await OwnerOfAsync(safe)).IsEqualTo(playerDbRef.Number);
			await Assert.That(await AttributeOwnerAsync(new DBRef(ProbateJudgeDbRefNumber), attrName)).IsEqualTo(playerDbRef.Number);
		}
		finally
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wipe #{ProbateJudgeDbRefNumber}/{attrName}"));
		}
	}

	[Test]
	public async Task Nuke_Player_NonSafePossession_IsMarkedAsGoing()
	{
		var playerDbRef = await CreateTestPlayerAsync("NonSafePossession");

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain("@create PDT_NonSafeThing_PossessionTest"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@chown {thingDbRef}={playerDbRef}"));

		var thingBeforeNuke = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Expect<AnySharpObject>();
		var ownerBefore = await thingBeforeNuke.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore.Object.DBRef.Number).IsEqualTo(playerDbRef.Number);

		// nuke the player  (destroy_possessions=yes)
		await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@nuke {playerDbRef}"));

		var thingAfterNuke = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Expect<AnySharpObject>();
		var isGoing = await thingAfterNuke.HasFlag("GOING");
		await Assert.That(isGoing).IsTrue();
	}

	[Test]
	public async Task Nuke_Player_SafePossession_IsNotMarked_AndGoesToProbateWhenFreed()
	{
		var playerDbRef = await CreateTestPlayerAsync("SafePossession");

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("PDT_SafeThing")}"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thingDbRef}=SAFE"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {thingDbRef}={playerDbRef}"));

		// really_safe=yes → SAFE things survive, so they are never scheduled
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));

		var thingAfterNuke = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Expect<AnySharpObject>();
		await Assert.That(await thingAfterNuke.HasFlag("GOING")).IsFalse();
		await Assert.That(await OwnerOfAsync(thingDbRef)).IsEqualTo(playerDbRef.Number);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));

		await Assert.That(await OwnerOfAsync(thingDbRef)).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	[Test]
	public async Task Nuke_Player_AttributeOwnerReassignedToProbatePlayer_OnlyWhenFreed()
	{
		var playerDbRef = await CreateTestPlayerAsync("AttrOwner");
		var testPlayer = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<SharpPlayer>();

		var createResult = await Parser.CommandParse(
			1, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("PDT_AttrHolder")}"));
		var thingDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// The test player authored an attribute on an object someone else owns.
		const string attrName = "PDT_ATTR_OWNER_TEST";
		await Database.SetAttributeAsync(thingDbRef, [attrName], MarkupText.Plain("authored"), testPlayer);
		await Assert.That(await AttributeOwnerAsync(thingDbRef, attrName)).IsEqualTo(playerDbRef.Number);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));
		await Assert.That(await AttributeOwnerAsync(thingDbRef, attrName)).IsEqualTo(playerDbRef.Number);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));
		await Assert.That(await AttributeOwnerAsync(thingDbRef, attrName)).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	// Combined: a player who owns a channel, a non-SAFE thing and a SAFE thing, and authored an
	// attribute. The first @nuke only marks; the second frees the player and runs the whole probate.
	[Test]
	public async Task Nuke_Player_Twice_CombinedScenario_ProbatesEverything()
	{
		var playerDbRef = await CreateTestPlayerAsync("CombinedScenario");
		var testPlayer = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<SharpPlayer>();
		var channelName = TestIsolationHelpers.GenerateUniqueName("PDT_CombChan");
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(channelName), ["Open"], testPlayer));

		var nonSafeDbRef = DBRef.Parse((await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("PDT_CombNonSafe")}"))).Message!.ToPlainText());
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {nonSafeDbRef}={playerDbRef}"));

		var safeDbRef = DBRef.Parse((await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("PDT_CombSafe")}"))).Message!.ToPlainText());
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {safeDbRef}=SAFE"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {safeDbRef}={playerDbRef}"));

		var holder = new DBRef(ProbateJudgeDbRefNumber);
		var attrName = $"PDT_COMB_{Guid.NewGuid():N}";
		await Database.SetAttributeAsync(holder, [attrName], MarkupText.Plain("authored"), testPlayer);

		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));

			var playerObj = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<AnySharpObject>();
			await Assert.That(await playerObj.HasFlag("GOING")).IsTrue();
			var nonSafeObj = (await Mediator.Send(new GetObjectNodeQuery(nonSafeDbRef))).Expect<AnySharpObject>();
			await Assert.That(await nonSafeObj.HasFlag("GOING")).IsTrue();
			var safeObj = (await Mediator.Send(new GetObjectNodeQuery(safeDbRef))).Expect<AnySharpObject>();
			await Assert.That(await safeObj.HasFlag("GOING")).IsFalse();

			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nuke {playerDbRef}"));

			await Assert.That((await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).IsNone).IsTrue();
			await Assert.That(await ChannelOwnerAsync(channelName)).IsEqualTo(ProbateJudgeDbRefNumber);
			await Assert.That((await Mediator.Send(new GetObjectNodeQuery(nonSafeDbRef))).IsNone).IsTrue()
				.Because("clear_player frees what destroy_possessions dooms");
			await Assert.That(await OwnerOfAsync(safeDbRef)).IsEqualTo(ProbateJudgeDbRefNumber);
			await Assert.That(await AttributeOwnerAsync(holder, attrName)).IsEqualTo(ProbateJudgeDbRefNumber);
		}
		finally
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wipe #{ProbateJudgeDbRefNumber}/{attrName}"));
		}
	}
}
