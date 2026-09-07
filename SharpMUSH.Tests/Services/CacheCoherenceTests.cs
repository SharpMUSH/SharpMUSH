using Mediator;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The two invalidation guarantees the behaviours give without a database: a key-invalidated object
/// entry never outlives a write that landed while its factory ran, and a write whose keys are only
/// known from its result (a create) still clears them. Real FusionCache, stub handlers.
/// </summary>
public class CacheCoherenceTests
{
	private static FusionCache NewCache() => new(new FusionCacheOptions
	{
		DefaultEntryOptions = CacheEntryProfiles.Tagged,
	}, new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheEntryProfiles.MemoryCacheSizeLimit }));

	private sealed record ObjectProbeQuery(int Number) : IQuery<string>, ICacheable
	{
		public string CacheKey => CacheKeys.Object(Number);
		public string[] CacheTags => [];
	}

	private sealed record CreateProbeCommand : ICommand<int>, ICacheInvalidating, ICacheInvalidatingByResult<int>
	{
		public string[] CacheKeys => [];
		public string[] CacheTags => [];
		public string[] CacheKeysFor(int created) => [SharpMUSH.Library.Definitions.CacheKeys.Object(created)];
	}

	[Test]
	public async Task AWriteThatLandsDuringTheFactoryRemovesTheEntryTheFactoryStored()
	{
		using var cache = NewCache();
		var versions = new ObjectVersions();
		var behaviour = new QueryCachingBehavior<ObjectProbeQuery, string>(cache, versions, Substitute.For<IMediator>());

		var served = await behaviour.Handle(new ObjectProbeQuery(7), (_, _) =>
		{
			// The database answered from before the commit; the write's invalidation lands before the store.
			versions.Bump(7);
			return ValueTask.FromResult("pre-write");
		}, CancellationToken.None);

		await Assert.That(served).IsEqualTo("pre-write")
			.Because("the caller that raced the write gets the answer the database gave it");
		await Assert.That((await cache.TryGetAsync<string>(CacheKeys.Object(7))).HasValue).IsFalse()
			.Because("but the entry stored from that answer is removed after the store, so nobody else does");
	}

	[Test]
	public async Task AnUndisturbedFactoryKeepsItsEntry()
	{
		using var cache = NewCache();
		var behaviour = new QueryCachingBehavior<ObjectProbeQuery, string>(cache, new ObjectVersions(), Substitute.For<IMediator>());

		await behaviour.Handle(new ObjectProbeQuery(7), (_, _) => ValueTask.FromResult("current"), CancellationToken.None);

		await Assert.That((await cache.TryGetAsync<string>(CacheKeys.Object(7))).Value).IsEqualTo("current");
	}

	private sealed record ContentsProbeQuery : IStreamQuery<AnySharpContent>, ICacheable
	{
		public string CacheKey => "contents-probe";
		public string[] CacheTags => [];
	}

	/// <summary>
	/// A contents list is stored as full object ids, so the lookup on read carries the creation
	/// milliseconds of the object the list named. A number recycled since then resolves to nothing:
	/// the object that took its place is not in the list.
	/// </summary>
	[Test]
	public async Task AStoredListLooksItsObjectsUpByFullObjectId()
	{
		using var cache = NewCache();
		var factory = new TestObjectFactory();
		var thing = factory.CreateThing(7, "Probe Thing");
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == 7 && q.DBRef.CreationMilliseconds == thing.Object().CreationTime), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(thing.AsThing));
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == 7 && q.DBRef.CreationMilliseconds != thing.Object().CreationTime), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(new OneOf.Types.None()));
		var behaviour = new StreamQueryCachingBehavior<ContentsProbeQuery, AnySharpContent>(cache, mediator);

		var listed = await behaviour.Handle(new ContentsProbeQuery(), (_, _) => new[] { thing.AsContent }.ToAsyncEnumerable(), CancellationToken.None).ToListAsync();

		await Assert.That(listed).Count().IsEqualTo(1);
		await Assert.That(ReferenceEquals(listed[0].Object(), thing.Object())).IsTrue();
		await mediator.Received(1).Send(
			Arg.Is<GetObjectNodeQuery>(q => q.DBRef.CreationMilliseconds == thing.Object().CreationTime), Arg.Any<CancellationToken>());

		// The same list, with the number recycled: the stored id no longer matches, so the list is empty.
		await cache.SetAsync("contents-probe", new CachedObjectRefs([new DBRef(7, thing.Object().CreationTime + 1)]), CacheEntryProfiles.Tagged);
		var recycled = await behaviour.Handle(new ContentsProbeQuery(), (_, _) => throw new InvalidOperationException("served from cache"), CancellationToken.None).ToListAsync();
		await Assert.That(recycled).IsEmpty();
	}

	[Test]
	public async Task AWriteRemovesTheKeysItsResultNames()
	{
		using var cache = NewCache();
		var versions = new ObjectVersions();
		var behaviour = new CacheInvalidationBehavior<CreateProbeCommand, int>(cache, versions);
		await cache.SetAsync(CacheKeys.Object(42), "resolved as missing before it existed", CacheEntryProfiles.Object);

		var created = await behaviour.Handle(new CreateProbeCommand(), (_, _) => ValueTask.FromResult(42), CancellationToken.None);

		await Assert.That(created).IsEqualTo(42);
		await Assert.That((await cache.TryGetAsync<string>(CacheKeys.Object(42))).HasValue).IsFalse();
		await Assert.That(versions.Of(42)).IsEqualTo(1)
			.Because("a result-derived key is an object key like any other and moves the object's version");
	}

	#region Attribute invalidation

	private static readonly DBRef Seven = new(7);
	private static readonly DBRef Eight = new(8);
	private static readonly string[] NestedPath = ["FOO", "BAR"];

	private static SharpAttribute AttributeNamed(string longName) =>
		new("attributes/1", "1", longName.Split('`')[^1], [], null, longName, null!, null!, null!);

	/// <summary>
	/// The reads that consult exactly one object. Their entries can be, and are, scoped to it.
	/// </summary>
	private static string[] DirectAttributeReadKeys(DBRef dbref) =>
	[
		new GetAttributeQuery(dbref, NestedPath).CacheKey,
		new GetLazyAttributeQuery(dbref, NestedPath).CacheKey
	];

	/// <summary>
	/// The reads that walk the parent and zone chains. Both <c>CheckParent</c> values are separate
	/// entries and a write has to name both.
	/// </summary>
	private static string[] InheritedAttributeReadKeys(DBRef dbref) =>
	[
		new GetAttributeWithInheritanceQuery(dbref, NestedPath, true).CacheKey,
		new GetAttributeWithInheritanceQuery(dbref, NestedPath, false).CacheKey,
		new GetLazyAttributeWithInheritanceQuery(dbref, NestedPath, true).CacheKey,
		new GetLazyAttributeWithInheritanceQuery(dbref, NestedPath, false).CacheKey
	];

	/// <summary>Every cached read of one object's attributes, by the key it stores under.</summary>
	private static string[] AttributeReadKeys(DBRef dbref) =>
		[.. DirectAttributeReadKeys(dbref), .. InheritedAttributeReadKeys(dbref)];

	private static async Task StoreUntagged(FusionCache cache, params string[] keys)
	{
		foreach (var key in keys)
		{
			await cache.SetAsync(key, "stored before the write", CacheEntryProfiles.Object);
		}
	}

	private static async Task<string[]> Surviving(FusionCache cache, IEnumerable<string> keys)
	{
		var alive = new List<string>();
		foreach (var key in keys)
		{
			if ((await cache.TryGetAsync<string>(key)).HasValue)
			{
				alive.Add(key);
			}
		}
		return [.. alive];
	}

	/// <summary>
	/// The keys are what a write has to get right. A tag removal only reaches entries that carry the
	/// tag, so these entries are stored WITHOUT one: if the command's keys do not name them, nothing
	/// does. Every attribute-mutating command spelled its key with a trailing <c>)</c> the readers
	/// never had, and the two path-wise commands keyed each segment instead of the joined path, so
	/// none of them ever removed the entry it was written to remove.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(AttributeWriteCommands))]
	public async Task AnAttributeWriteRemovesTheEntriesTheAttributeReadsStored(
		string _, Func<FusionCache, Task> write)
	{
		using var cache = NewCache();
		await StoreUntagged(cache, AttributeReadKeys(Seven));

		await write(cache);

		await Assert.That(await Surviving(cache, AttributeReadKeys(Seven))).IsEmpty();
	}

	public static IEnumerable<Func<(string, Func<FusionCache, Task>)>> AttributeWriteCommands()
	{
		yield return () => ("set",
			c => Invalidate(c, new SetAttributeCommand(Seven, NestedPath, MModule.single("v"), null!)));
		yield return () => ("clear",
			c => Invalidate(c, new ClearAttributeCommand(Seven, NestedPath)));
		yield return () => ("wipe",
			c => Invalidate(c, new WipeAttributeCommand(Seven, NestedPath)));
		yield return () => ("set-flag",
			c => Invalidate(c, new SetAttributeFlagCommand(Seven, AttributeNamed("FOO`BAR"), null!)));
		yield return () => ("unset-flag",
			c => Invalidate(c, new UnsetAttributeFlagCommand(Seven, AttributeNamed("FOO`BAR"), null!)));
	}

	private static async Task Invalidate<TCommand>(FusionCache cache, TCommand command)
		where TCommand : ICommand<bool>, ICacheInvalidating
		=> await new CacheInvalidationBehavior<TCommand, bool>(cache, new ObjectVersions())
			.Handle(command, (_, _) => ValueTask.FromResult(true), CancellationToken.None);

	/// <summary>
	/// The object's own listen set is cached under <c>listens:{dbref}</c> and is derived from its
	/// attributes, exactly like the command set beside it. Setting a <c>^pattern:action</c> has to
	/// reach it, or the object keeps listening on the pattern it no longer has.
	/// </summary>
	[Test]
	public async Task AnAttributeWriteRemovesTheObjectsCachedListenSet()
	{
		using var cache = NewCache();
		var factory = new TestObjectFactory();
		var listener = factory.CreateThing(7, "Listener");
		var key = new GetListenAttributesQuery(listener).CacheKey;
		await StoreUntagged(cache, key);

		await new CacheInvalidationBehavior<SetAttributeCommand, bool>(cache, new ObjectVersions())
			.Handle(new SetAttributeCommand(Seven, ["LISTEN"], MModule.single("*"), null!),
				(_, _) => ValueTask.FromResult(true), CancellationToken.None);

		await Assert.That((await cache.TryGetAsync<string>(key)).HasValue).IsFalse();
	}

	/// <summary>
	/// Attribute entries are tagged so that a write which raced the read still expires them, but the
	/// tag has to name the object the write touched. A single game-wide <c>object-attributes</c> tag
	/// made every <c>&amp;ATTR</c> anywhere drop every object's cached attributes — on a live game
	/// that is continuous, so the most-read key family in the engine never accumulated a hit.
	/// </summary>
	[Test]
	public async Task AnAttributeWriteLeavesAnotherObjectsDirectlyReadAttributesAlone()
	{
		using var cache = NewCache();
		var read = new GetAttributeQuery(Eight, NestedPath);
		foreach (var key in DirectAttributeReadKeys(Eight))
		{
			await cache.SetAsync(key, "another object's attribute", CacheEntryProfiles.Tagged,
				tags: read.CacheTags);
		}

		await new CacheInvalidationBehavior<SetAttributeCommand, bool>(cache, new ObjectVersions())
			.Handle(new SetAttributeCommand(Seven, NestedPath, MModule.single("v"), null!),
				(_, _) => ValueTask.FromResult(true), CancellationToken.None);

		await Assert.That(await Surviving(cache, DirectAttributeReadKeys(Eight)))
			.IsEquivalentTo(DirectAttributeReadKeys(Eight));
	}

	/// <summary>
	/// The inherited reads deliberately keep a game-wide tag, and this pins that so it is a decision
	/// rather than an oversight. An inheritance entry answers "what does this object see for ATTR,
	/// counting its parents and zones" — a write to any object in that chain changes the answer, and
	/// so does a write that makes a NEARER ancestor shadow a further one. The chain the read walked is
	/// not in its result (a not-found answer is an empty stream), so there is nothing to scope the tag
	/// by until the providers project the objects they visited. Until then, over-expiring is the only
	/// correct choice: a write to a parent must reach every child's entry.
	/// </summary>
	[Test]
	public async Task AnAttributeWriteStillExpiresEveryObjectsInheritedAttributeEntries()
	{
		using var cache = NewCache();
		var read = new GetAttributeWithInheritanceQuery(Eight, NestedPath);
		foreach (var key in InheritedAttributeReadKeys(Eight))
		{
			await cache.SetAsync(key, "an answer that counted #7 as an ancestor", CacheEntryProfiles.Tagged,
				tags: read.CacheTags);
		}

		await new CacheInvalidationBehavior<SetAttributeCommand, bool>(cache, new ObjectVersions())
			.Handle(new SetAttributeCommand(Seven, NestedPath, MModule.single("v"), null!),
				(_, _) => ValueTask.FromResult(true), CancellationToken.None);

		await Assert.That(await Surviving(cache, InheritedAttributeReadKeys(Eight))).IsEmpty();
	}

	/// <summary>
	/// Setting <c>FOO`BAR</c> changes <c>FOO</c>: it gains a leaf, and the branch flag with it. The
	/// entry cached for the parent path has to go too, so a write names every prefix of its path.
	/// </summary>
	[Test]
	public async Task SettingANestedAttributeExpiresTheEntryCachedForItsParentPath()
	{
		using var cache = NewCache();
		var parent = new GetAttributeQuery(Seven, ["FOO"]).CacheKey;
		await StoreUntagged(cache, parent);

		await new CacheInvalidationBehavior<SetAttributeCommand, bool>(cache, new ObjectVersions())
			.Handle(new SetAttributeCommand(Seven, NestedPath, MModule.single("v"), null!),
				(_, _) => ValueTask.FromResult(true), CancellationToken.None);

		await Assert.That((await cache.TryGetAsync<string>(parent)).HasValue).IsFalse();
	}

	/// <summary>
	/// The other half of the same rule: a tagged entry for the object that WAS written must still go,
	/// which is what protects a read that began before the write and stored its answer after it.
	/// </summary>
	[Test]
	public async Task AnAttributeWriteStillExpiresTheWrittenObjectsTaggedEntries()
	{
		using var cache = NewCache();
		var read = new GetAttributeQuery(Seven, NestedPath);
		await cache.SetAsync(read.CacheKey, "raced the write", CacheEntryProfiles.Tagged, tags: read.CacheTags);

		await new CacheInvalidationBehavior<SetAttributeCommand, bool>(cache, new ObjectVersions())
			.Handle(new SetAttributeCommand(Seven, NestedPath, MModule.single("v"), null!),
				(_, _) => ValueTask.FromResult(true), CancellationToken.None);

		await Assert.That((await cache.TryGetAsync<string>(read.CacheKey)).HasValue).IsFalse();
	}

	/// <summary>
	/// Reassigning every attribute a player owns changes what the cached attributes say about their
	/// owner, and attribute permission checks read it. It cannot name the objects it touched, so it is
	/// the one write that still sweeps every object's attribute entries.
	/// </summary>
	[Test]
	public async Task ReassigningAttributeOwnershipExpiresEveryObjectsCachedAttributes()
	{
		using var cache = NewCache();
		var read = new GetAttributeQuery(Eight, NestedPath);
		await cache.SetAsync(read.CacheKey, "owned by the old owner", CacheEntryProfiles.Tagged,
			tags: read.CacheTags);

		await new CacheInvalidationBehavior<ReassignAttributeOwnerCommand, Unit>(cache, new ObjectVersions())
			.Handle(new ReassignAttributeOwnerCommand(null!, null!),
				(_, _) => ValueTask.FromResult(Unit.Value), CancellationToken.None);

		await Assert.That((await cache.TryGetAsync<string>(read.CacheKey)).HasValue).IsFalse();
	}

	#endregion
}
