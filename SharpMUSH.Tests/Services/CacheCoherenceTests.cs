using Mediator;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
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
}
