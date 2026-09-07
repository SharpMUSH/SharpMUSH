using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// A channel's members and each member's own status, when there is more than one of them.
/// </summary>
/// <remarks>
/// The member list is projected in one query rather than fetched an object at a time, so the thing
/// that can go wrong is the pairing: every member has to arrive with its own membership edge and its
/// own object, not its neighbour's. One member cannot show that.
/// </remarks>
public class ChannelMemberProjectionTests
{
	private const string ChannelName = "MemberProjection";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	[NotInParallel]
	public async Task EveryMemberArrivesWithItsOwnObjectAndItsOwnStatus()
	{
		var ownerNode = await Database.GetObjectNodeAsync(new DBRef(1));
		var owner = ownerNode.AsPlayer;
		var home = ownerNode.Known().AsContainer;
		await Mediator.Send(new CreateChannelCommand(MModule.single(ChannelName), ["Open"], owner));

		var channel = await Mediator.Send(new GetChannelQuery(ChannelName));
		await Assert.That(channel).IsNotNull();

		// Two more members, so the projection has to keep three edges and three objects in step.
		var extras = new List<AnySharpObject>();
		foreach (var name in new[] { "ProjectionAlpha", "ProjectionBeta" })
		{
			var created = await Mediator.Send(new CreateThingCommand(name, home, owner, home));
			var thing = await Mediator.Send(new GetObjectNodeQuery(created));
			extras.Add(thing.Known());
			await Mediator.Send(new AddUserToChannelCommand(channel!, thing.Known()));
		}

		// One of them gagged, so a status swapped between members would show.
		await Mediator.Send(new UpdateChannelUserStatusCommand(channel!, extras[0],
			new SharpChannelStatus(Combine: null, Gagged: true, Hide: null, Mute: null, Title: null)));

		var reread = await Mediator.Send(new GetChannelQuery(ChannelName));
		var members = await reread!.Members.Value.ToArrayAsync();

		await Assert.That(members.Select(m => m.Member.Object().Name))
			.Contains("ProjectionAlpha").And.Contains("ProjectionBeta");

		var alpha = members.Single(m => m.Member.Object().Name == "ProjectionAlpha");
		var beta = members.Single(m => m.Member.Object().Name == "ProjectionBeta");

		await Assert.That(alpha.Status.Gagged).IsTrue()
			.Because("the gag was set on this member, and it must not travel to another");
		await Assert.That(beta.Status.Gagged ?? false).IsFalse();

		// The objects are real, not bare documents: a member arrives with the relations that let a
		// permission check read its flags without going back to storage.
		await Assert.That(await alpha.Member.Object().Flags.Value.ToArrayAsync()).IsNotNull();
		await Assert.That(alpha.Member.Object().DBRef.Number)
			.IsNotEqualTo(beta.Member.Object().DBRef.Number);
	}
}
