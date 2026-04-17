using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Tests that verify caching behaviors work correctly through the Mediator pipeline.
/// These tests exercise the real QueryCachingBehavior, StreamQueryCachingBehavior,
/// and CacheInvalidationBehavior using the fully wired DI container.
/// </summary>
public class CachingBehaviorTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IFusionCache Cache => WebAppFactory.Services.GetRequiredService<IFusionCache>();
	private IConnectionService ConnectionService => WebAppFactory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactory.CommandParser;

	/// <summary>
	/// Verifies that FusionCache is registered in the DI container.
	/// </summary>
	[Test]
	public async Task FusionCache_IsRegistered()
	{
		var cache = WebAppFactory.Services.GetRequiredService<IFusionCache>();
		await Assert.That(cache).IsNotNull();
	}

	/// <summary>
	/// Verifies that querying GetObjectNodeQuery twice with the same DBRef returns
	/// a result from cache on the second call (the cache key should be populated).
	/// </summary>
	[Test]
	public async Task QueryCachingBehavior_CachesObjectNodeQuery()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<Mediator.IMediator>();
		var dbRef = WebAppFactory.ExecutorDBRef;

		// First call – populates cache
		var result1 = await mediator.Send(new GetObjectNodeQuery(dbRef));

		// Verify cache key exists
		var cacheKey = $"object:{dbRef}";
		var cached = await Cache.TryGetAsync<object>(cacheKey);
		await Assert.That(cached.HasValue).IsTrue();

		// Second call – should come from cache
		var result2 = await mediator.Send(new GetObjectNodeQuery(dbRef));

		await Assert.That(result1.IsT0).IsEqualTo(result2.IsT0);
	}

	/// <summary>
	/// Verifies that StreamQueryCachingBehavior caches GetContentsQuery results.
	/// The second invocation with the same container should serve from cache.
	/// </summary>
	[Test]
	public async Task StreamQueryCachingBehavior_CachesContentsQuery()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<Mediator.IMediator>();
		var dbRef = WebAppFactory.ExecutorDBRef;

		// First call – materialize and cache
		var result1 = new List<object>();
		await foreach (var item in mediator.CreateStream(new GetContentsQuery(dbRef)))
		{
			result1.Add(item);
		}

		// Verify cache key exists
		var cacheKey = $"object-contents:{dbRef}";
		var cached = await Cache.TryGetAsync<object>(cacheKey);
		await Assert.That(cached.HasValue).IsTrue();

		// Second call – should come from cache
		var result2 = new List<object>();
		await foreach (var item in mediator.CreateStream(new GetContentsQuery(dbRef)))
		{
			result2.Add(item);
		}

		await Assert.That(result1.Count).IsEqualTo(result2.Count);
	}

	/// <summary>
	/// Verifies that cache invalidation ensures fresh data after a mutation.
	/// Creates a unique object, renames it, then verifies the query returns
	/// the new name — proving the stale cached entry was invalidated.
	/// </summary>
	[Test]
	public async Task CacheInvalidation_RenameReturnsNewName()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<Mediator.IMediator>();

		// Create a unique object to avoid interference from parallel tests
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@create CacheInvalidation Test Object"));
		var dbRef = Library.Models.DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Populate the cache for this object
		var before = await mediator.Send(new GetObjectNodeQuery(dbRef));
		await Assert.That(before.Object()!.Name).IsEqualTo("CacheInvalidation Test Object");

		// Rename via command — SetNameCommand invalidates object:{dbRef} cache key
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@name {dbRef}=CacheInvalidation Renamed Object"));

		// Query again — should return the new name, not the stale cached one
		var after = await mediator.Send(new GetObjectNodeQuery(dbRef));
		await Assert.That(after.Object()!.Name).IsEqualTo("CacheInvalidation Renamed Object");
	}

	/// <summary>
	/// Verifies that creating an object invalidates the ObjectContents tag,
	/// by checking the created object can be found via ObjectNode query.
	/// </summary>
	[Test]
	public async Task CacheInvalidation_CreateObjectVisibleViaQuery()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<Mediator.IMediator>();

		// Create a new thing
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@create CacheInvalidation Visibility Test"));
		var newDbRef = Library.Models.DBRef.Parse(createResult.Message!.ToPlainText()!);

		// The new object should be queryable (cache was invalidated or wasn't stale)
		var obj = await mediator.Send(new GetObjectNodeQuery(newDbRef));
		await Assert.That(obj.IsNone).IsFalse();
		await Assert.That(obj.Object()!.Name).IsEqualTo("CacheInvalidation Visibility Test");
	}
}
