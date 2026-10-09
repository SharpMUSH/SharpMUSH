using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// A cached object-shaped query is stored as the refs it names and resolved through the object node cache
/// (see <c>IObjectShaped</c>). On a miss the handler must not hydrate the objects itself only for the caching
/// behaviour to throw them away and hydrate them again: each object it names is built once, by the node
/// cache, and the behaviour's re-resolve is a hit (#1554).
/// </summary>
/// <remarks>
/// Counted with the provider's <see cref="ObjectReadStats"/> on objects this test created and watches, so
/// what other tests load in the shared host does not reach the counts. The objects are written straight
/// to the store and their cache entries removed, so both the list and the node cache start cold. A world
/// scan another test runs can only add a hydration, never remove one, so each measurement takes the best of
/// a few fresh attempts.
/// </remarks>
public class ShapedQueryHydrationTests
{
	private const int Attempts = 3;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private LightningDatabase Database => WebAppFactoryArg.Services.GetRequiredService<LightningDatabase>();
	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
	private IFusionCache Cache => WebAppFactoryArg.Services.GetRequiredService<IFusionCache>();

	private async Task<SharpPlayer> God() => (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<AnySharpObject> Node(DBRef dbref) => (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private async Task<DBRef> Room(string name) => await Database.CreateRoomAsync(TestIsolationHelpers.GenerateUniqueName(name), await God());

	private async Task<DBRef> Thing(string name, DBRef room)
	{
		var container = (await Node(room)).AsOptionalContainer.Expect<AnySharpContainer>();
		return await Database.CreateThingAsync(TestIsolationHelpers.GenerateUniqueName(name), container, await God(), container);
	}

	/// <summary>Watches <paramref name="objects"/> and drops every cache entry that could already answer for them.</summary>
	private async Task Cold(IEnumerable<string> keys, params DBRef[] objects)
	{
		foreach (var dbref in objects)
		{
			Database.ObjectStats.Watch(dbref.Number);
			await Cache.RemoveAsync(new GetObjectNodeByNumberQuery(dbref.Number).CacheKey);
		}

		foreach (var key in keys)
		{
			await Cache.RemoveAsync(key);
		}
	}

	private long Hydrations(DBRef dbref) => Database.ObjectStats.HydrationsOf(dbref.Number);

	[Test]
	public async Task AContentsMissHydratesEachObjectOnce()
	{
		var best = long.MaxValue;
		for (var attempt = 0; attempt < Attempts && best != 1; attempt++)
		{
			var room = await Room("HydrateOnceRoom");
			DBRef[] things = [await Thing("HydrateOnceA", room), await Thing("HydrateOnceB", room), await Thing("HydrateOnceC", room)];
			var query = new GetContentsQuery(room);
			await Cold([query.CacheKey], things);

			var listed = await Mediator.CreateStream(query).ToArrayAsync();

			await Assert.That(listed.Select(content => content.Object().DBRef)).IsEquivalentTo(things);
			foreach (var content in listed)
			{
				var node = (await Mediator.Send(new GetObjectNodeQuery(content.Object().DBRef))).Expect<AnySharpObject>();
				await Assert.That(ReferenceEquals(content.Object(), node.Object())).IsTrue()
					.Because("a contents list hands out the node cache's one instance of each object");
			}

			best = Math.Min(best, things.Max(Hydrations));
		}

		await Assert.That(best).IsEqualTo(1)
			.Because("the handler streams refs and the node cache builds each object once; the behaviour's re-resolve hits");
	}

	[Test]
	public async Task ALocationMissHydratesTheContainerOnce()
	{
		var best = long.MaxValue;
		for (var attempt = 0; attempt < Attempts && best != 1; attempt++)
		{
			var room = await Room("HydrateOnceLocation");
			var thing = await Thing("HydrateOnceOccupant", room);
			var query = new GetLocationQuery(thing);
			await Cold([query.CacheKey], room);

			var located = (await Mediator.Send(query)).Expect<AnySharpContainer>();

			await Assert.That(located.Object().DBRef).IsEqualTo(room);
			best = Math.Min(best, Hydrations(room));
		}

		await Assert.That(best).IsEqualTo(1)
			.Because("a single-relation miss resolves the ref through the node cache, which builds the room once");
	}

	[Test]
	public async Task AParentMissHydratesTheParentOnce()
	{
		var best = long.MaxValue;
		for (var attempt = 0; attempt < Attempts && best != 1; attempt++)
		{
			var room = await Room("HydrateOnceParentRoom");
			var parent = await Thing("HydrateOnceParent", room);
			var child = await Thing("HydrateOnceChild", room);
			await Database.SetObjectParent(await Node(child), await Node(parent));
			var query = new GetParentOfQuery(child.Number.ToString(), child.Number);
			await Cold([query.CacheKey], parent);

			var found = (await Mediator.Send(query)).Expect<AnySharpObject>();

			await Assert.That(found.Object().DBRef).IsEqualTo(parent);
			best = Math.Min(best, Hydrations(parent));
		}

		await Assert.That(best).IsEqualTo(1)
			.Because("a relation miss resolves the parent's ref through the node cache, which builds it once");
	}
}
