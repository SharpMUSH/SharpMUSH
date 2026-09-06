using Mediator;
using Microsoft.Extensions.Caching.Memory;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
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
		var behaviour = new QueryCachingBehavior<ObjectProbeQuery, string>(cache, versions);

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
		var behaviour = new QueryCachingBehavior<ObjectProbeQuery, string>(cache, new ObjectVersions());

		await behaviour.Handle(new ObjectProbeQuery(7), (_, _) => ValueTask.FromResult("current"), CancellationToken.None);

		await Assert.That((await cache.TryGetAsync<string>(CacheKeys.Object(7))).Value).IsEqualTo("current");
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
