using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A channel always has an owner. Deleting the owner hands the channel on; it never orphans it.
/// </summary>
/// <remarks>
/// Deleting an object severs every relationship on it, and channel ownership rode along: the channel
/// survived with nothing on the other end, every provider threw out of the row access, and because
/// <c>@channel/add</c> resolves the owner of <em>every</em> channel to count the ones the executor owns,
/// a single orphan broke channel creation for the whole session. That is what made
/// <c>ChannelAddStoresPrivilegesInCanonicalCasing</c> and <c>MortalCannotJoinOrSpeakOnAnyGatedChannel</c>
/// flake.
/// <para>
/// The fix is to keep the invariant rather than to model its absence.
/// <c>ObjectDestructionService.ClearPlayerAsync</c> already handed a doomed player's channels to the
/// probate judge; <c>DeleteObjectAsync</c> now does the same to God for every other route to a delete,
/// so ownership cannot die with the owner whichever way the object goes.
/// </para>
/// </remarks>
public class ChannelOwnerlessTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static string UniqueChannel(string prefix)
		=> TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	private async Task<DBRef> ChannelOwnedBy(string name, DBRef ownerDbRef)
	{
		var owner = (await Mediator.Send(new GetObjectNodeQuery(ownerDbRef))).AsPlayer;
		await Mediator.Send(new CreateChannelCommand(MModule.single(name), ["Player"], owner));
		return ownerDbRef;
	}

	/// <summary>
	/// Deleting a channel's owner through storage re-owns the channel rather than orphaning it.
	/// </summary>
	/// <remarks>
	/// The raw <see cref="DeleteObjectCommand"/> deliberately, because it is the route that bypasses
	/// <c>ObjectDestructionService</c>'s probate hand-off — the floor has to hold on its own.
	/// </remarks>
	[Test]
	public async Task DeletingAnOwnerHandsTheChannelOnRatherThanOrphaningIt()
	{
		var name = UniqueChannel("Ownerless");

		var doomed = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "OwnerlessChanOwner");
		await ChannelOwnedBy(name, doomed);

		await Mediator.Send(new DeleteObjectCommand(doomed));

		var channel = await Mediator.Send(new GetChannelQuery(name));
		await Assert.That(channel).IsNotNull()
			.Because("deleting the owner destroys the owner, not the channel");

		// What @channel/add does to every channel in the list before it creates one.
		var owner = await channel!.Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(1)
			.Because("ownership cannot die with the owner, so God inherits what nobody else claimed");
	}

	/// <summary>
	/// The whole channel list stays readable after an owner is deleted.
	/// </summary>
	/// <remarks>
	/// The list is the shape that actually broke: <c>@channel/add</c> resolves every owner in it, so one
	/// unreadable channel is enough to stop anyone creating one.
	/// </remarks>
	[Test]
	public async Task TheChannelListStaysReadableAfterAnOwnerIsDeleted()
	{
		var name = UniqueChannel("ListSurvives");

		var doomed = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "ListSurvivesOwner");
		await ChannelOwnedBy(name, doomed);

		await Mediator.Send(new DeleteObjectCommand(doomed));

		var owners = new List<int>();
		await foreach (var listed in Mediator.CreateStream(new GetChannelListQuery()))
		{
			owners.Add((await listed.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number);
		}

		await Assert.That(owners).IsNotEmpty()
			.Because("resolving every owner in the list must not throw for any channel in it");
	}

	/// <summary>
	/// Re-owning works on a channel whose owner edge is already missing.
	/// </summary>
	/// <remarks>
	/// The repair path for data that predates the invariant being enforced. Memgraph gated its
	/// <c>CREATE</c> on finding an existing owner edge, so re-owning such a channel was a silent no-op;
	/// ArangoDB called <c>First()</c> on the empty edge list and threw. Both <c>@channel/chown</c> and
	/// <c>ObjectDestructionService</c> arrive at <c>UpdateChannelOwnerAsync</c> to do it.
	/// </remarks>
	[Test]
	public async Task ReOwningWorksOnAChannelThatHasLostItsOwnerEdge()
	{
		var name = UniqueChannel("Reowned");

		var doomed = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "ReownedChanOwner");
		await ChannelOwnedBy(name, doomed);
		await Mediator.Send(new DeleteObjectCommand(doomed));

		var channel = (await Mediator.Send(new GetChannelQuery(name)))!;
		var heir = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "ReownedChanHeir");
		var heirPlayer = (await Mediator.Send(new GetObjectNodeQuery(heir))).AsPlayer;

		await Mediator.Send(new UpdateChannelOwnerCommand(channel, heirPlayer));

		var reowned = (await Mediator.Send(new GetChannelQuery(name)))!;
		var owner = await reowned.Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(heir.Number)
			.Because("re-owning a channel is the repair, and must not be a no-op");
	}
}
