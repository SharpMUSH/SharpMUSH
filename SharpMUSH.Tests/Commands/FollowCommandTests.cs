using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>FOLLOWING</c> is seeded with the <c>wizard</c> attribute flag
/// (<c>SurrealDatabase.Migration.cs:718</c> and its Memgraph/Arango counterparts), which is why
/// PennMUSH writes it as GOD rather than as the player: <c>atr_add(follower, "FOLLOWING", …, GOD, 0)</c>
/// (<c>src/move.c:1236</c>) and <c>atr_clr(follower, "FOLLOWING", GOD)</c> (<c>src/move.c:1451</c>).
/// <para>
/// Before the attribute-tree write gate started honouring <c>AF_WIZARD</c>, routing these writes
/// through the executor happened to work because nothing tested the flag. Once it did, every
/// mortal FOLLOW / UNFOLLOW / DESERT / DISMISS silently failed while still reporting success —
/// the commands discarded <c>ClearAttributeAsync</c>'s result. These tests pin the engine-authorized
/// path so that regression cannot come back unnoticed.
/// </para>
/// </summary>
public class FollowCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private async Task<string> NameOf(DBRef dbRef)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbRef))).Known.Object().Name;

	private async Task<AttributeReadResult> FollowingOf(DBRef who)
	{
		var obj = await Mediator.Send(new GetObjectNodeQuery(who));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOLLOWING",
			IAttributeService.AttributeMode.Read, false);

		return new AttributeReadResult(
			attr.IsAttribute,
			attr.IsAttribute ? attr.AsAttribute.Last().Value.ToPlainText() : null,
			attr.IsAttribute && attr.AsAttribute.Last().Flags
				.Any(f => f.Name.Equals("wizard", StringComparison.OrdinalIgnoreCase)));
	}

	private record AttributeReadResult(bool Exists, string? Value, bool IsWizardFlagged);

	[Test]
	public async ValueTask MortalFollow_OverwritesTheWizardFlaggedAttribute()
	{
		var follower = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FollowMortal");
		var first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FollowLeaderA");
		var second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FollowLeaderB");

		await Parser.CommandParse(follower.Handle, ConnectionService,
			MModule.single($"follow {await NameOf(first.DbRef)}"));

		var afterFirst = await FollowingOf(follower.DbRef);
		await Assert.That(afterFirst.Value).IsEqualTo(first.DbRef.ToString())
			.Because("FOLLOWING holds the dbref of whoever is being followed");

		// Positive control: the seeded wizard flag is only applied once the attribute EXISTS, so
		// the very first follow creates it before any AF_WIZARD test can see it. Switching leaders
		// is the write that actually crosses the gate - and the case a player hits constantly.
		await Assert.That(afterFirst.IsWizardFlagged).IsTrue()
			.Because("FOLLOWING is seeded wizard-flagged - that is the gate the second follow must cross");

		await Parser.CommandParse(follower.Handle, ConnectionService,
			MModule.single($"follow {await NameOf(second.DbRef)}"));

		var afterSecond = await FollowingOf(follower.DbRef);
		await Assert.That(afterSecond.Value).IsEqualTo(second.DbRef.ToString())
			.Because("a mortal must be able to change who they follow: PennMUSH writes FOLLOWING as GOD");
	}

	[Test]
	public async ValueTask MortalUnfollow_ClearsTheWizardFlaggedAttribute()
	{
		var follower = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "UnfollowMortal");
		var leader = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "UnfollowLeader");

		await Parser.CommandParse(follower.Handle, ConnectionService,
			MModule.single($"follow {await NameOf(leader.DbRef)}"));

		var before = await FollowingOf(follower.DbRef);
		await Assert.That(before.Exists).IsTrue()
			.Because("precondition: there is nothing to unfollow unless the follow landed");

		await Parser.CommandParse(follower.Handle, ConnectionService, MModule.single("unfollow"));

		var after = await FollowingOf(follower.DbRef);
		await Assert.That(after.Exists).IsFalse()
			.Because("a mortal must be able to stop following - atr_clr(follower, \"FOLLOWING\", GOD)");
	}
}
