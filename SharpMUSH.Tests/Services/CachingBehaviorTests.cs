using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
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
[NotInParallel]
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
	/// Uses a freshly created object to avoid interference from concurrent tests that
	/// might invalidate the executor's well-known cache key.
	/// </summary>
	[Test]
	public async Task QueryCachingBehavior_CachesObjectNodeQuery()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<Mediator.IMediator>();

		// Create a unique object so no other parallel test can invalidate its specific cache key
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@create QueryCachingBehavior Test Object"));
		var dbRef = Library.Models.DBRef.Parse(createResult.Message!.ToPlainText()!);

		// First call – populates cache
		var result1 = await mediator.Send(new GetObjectNodeQuery(dbRef));

		// Verify cache key exists
		var cacheKey = $"object:{dbRef}";
		var cached = await Cache.TryGetAsync<AnyOptionalSharpObject>(cacheKey);
		await Assert.That(cached.HasValue).IsTrue();

		// Second call – should come from cache
		var result2 = await mediator.Send(new GetObjectNodeQuery(dbRef));

		await Assert.That(result1.IsT0).IsEqualTo(result2.IsT0);
	}

	/// <summary>
	/// Verifies that StreamQueryCachingBehavior caches GetContentsQuery results.
	/// The second invocation with the same container should serve from cache.
	/// Uses a freshly created room to avoid interference from concurrent tests.
	/// </summary>
	[Test]
	public async Task StreamQueryCachingBehavior_CachesContentsQuery()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<Mediator.IMediator>();

		// Dig a unique room so no other parallel test can invalidate its specific cache key
		var digResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dig StreamCachingBehavior Test Room"));
		var dbRef = Library.Models.DBRef.Parse(digResult.Message!.ToPlainText()!);

		// First call – materialize and cache
		var result1 = new List<AnySharpContent>();
		await foreach (var item in mediator.CreateStream(new GetContentsQuery(dbRef)))
		{
			result1.Add(item);
		}

		// Verify cache key exists. The room is unique to this test so no other test can invalidate
		// this specific key via a targeted CacheKey. Multiple retries guard against the
		// MoveObjectCommand fallback ObjectContents tag sweep, which fires for all callers that
		// don't supply OldContainer (GeneralCommands, MoreCommands, UtilityFunctions) and is
		// common under parallel CI load.
		var cacheKey = $"object-contents:{dbRef}";
		var cached = await Cache.TryGetAsync<List<AnySharpContent>>(cacheKey);
		for (var retry = 0; !cached.HasValue && retry < 10; retry++)
		{
			result1.Clear();
			await foreach (var item in mediator.CreateStream(new GetContentsQuery(dbRef)))
			{
				result1.Add(item);
			}
			cached = await Cache.TryGetAsync<List<AnySharpContent>>(cacheKey);
		}

		await Assert.That(cached.HasValue).IsTrue();

		// Second call – should come from cache
		var result2 = new List<AnySharpContent>();
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
	/// Verifies that a newly created object can be queried via GetObjectNodeQuery,
	/// confirming that cache entries for the new object are populated correctly after creation.
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
