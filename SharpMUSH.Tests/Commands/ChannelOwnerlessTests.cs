using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A channel whose owner has been destroyed must still be readable.
/// </summary>
/// <remarks>
/// <c>SharpChannel.Owner</c> is an <see cref="AsyncLazy{T}"/> that queries the database when it is first
/// awaited, so a channel that has been listed is not yet fully materialised — and the owner edge can be
/// gone by the time anyone asks. Destroying a player is the deterministic way there: the delete detaches
/// every relationship on the object, <c>HAS_CHANNEL_OWNER</c> included, and leaves the channel node
/// behind with nothing on the other end.
/// <para>
/// Every provider then threw out of the row access — Memgraph indexing <c>Result[0]</c>, ArangoDB
/// calling <c>First()</c> — and because <c>@channel/add</c> resolves the owner of <em>every</em> channel
/// to count the ones the executor owns, a single ownerless channel broke channel creation for everybody
/// from then on. That is what made <c>ChannelAddStoresPrivilegesInCanonicalCasing</c> and
/// <c>MortalCannotJoinOrSpeakOnAnyGatedChannel</c> flake: they failed whenever some earlier test in the
/// session had destroyed a channel owner.
/// </para>
/// </remarks>
public class ChannelOwnerlessTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task AChannelOutlivingItsOwnerIsStillReadable()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("Ownerless").Replace("_", string.Empty);

		// Deliberately no connection handle: this player gets deleted out from under the database, and a
		// handle still bound to a destroyed dbref would leave a phantom in WHO for the rest of the session.
		var ownerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "OwnerlessChanOwner");
		var ownerObject = (await Mediator.Send(new GetObjectNodeQuery(ownerDbRef))).AsPlayer;

		await Mediator.Send(new CreateChannelCommand(MModule.single(name), ["Player"], ownerObject));
		await Mediator.Send(new DeleteObjectCommand(ownerDbRef));

		var listed = await Mediator.CreateStream(new GetChannelListQuery())
			.Where(c => c.Name.ToPlainText() == name)
			.FirstOrDefaultAsync();

		await Assert.That(listed).IsNotNull()
			.Because("destroying the owner destroys the owner, not the channel");

		// What @channel/add does to every channel in the list before it creates one.
		var resolvedOwner = await listed!.Owner.WithCancellation(CancellationToken.None);

		await Assert.That(resolvedOwner).IsNull()
			.Because("an ownerless channel has no owner to report, and asking must not throw");
	}
}
