using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Tests.Infrastructure;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Handlers;

/// <summary>
/// The <c>$</c>-command Nearby scope: the object, its contents, then its location's contents without the object
/// itself — composed from the cached node and contents queries, in that order.
/// </summary>
public class NearbyObjectsQueryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	[Test]
	public async Task NearbyIsSelfThenContentsThenTheLocationsOtherContents()
	{
		var god = (await Node(new DBRef(1))).Expect<SharpPlayer>();
		var room = (await Node(await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("NearRoom"), god)))).AsContainer;
		async Task<DBRef> Thing(string prefix, AnySharpContainer where)
			=> await Mediator.Send(new CreateThingCommand(TestIsolationHelpers.GenerateUniqueName(prefix), where, god, room));

		var self = await Thing("NearSelf", room);
		var before = await Thing("NearBefore", room);
		var after = await Thing("NearAfter", room);
		var selfContainer = (await Node(self)).AsContainer;
		var carried = await Thing("NearCarried", selfContainer);

		var byRef = await Mediator.CreateStream(new GetNearbyObjectsQuery(self)).Select(o => o.Object().DBRef.Number).ToListAsync();
		var byObject = await Mediator.CreateStream(new GetNearbyObjectsQuery(await Node(self))).Select(o => o.Object().DBRef.Number).ToListAsync();

		int[] expected = [self.Number, carried.Number, before.Number, after.Number];
		await Assert.That(byRef).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(byObject).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task NearbyOfAMissingObjectIsEmpty()
		=> await Assert.That(await Mediator.CreateStream(new GetNearbyObjectsQuery(new DBRef(int.MaxValue - 7))).CountAsync()).IsEqualTo(0);
}
