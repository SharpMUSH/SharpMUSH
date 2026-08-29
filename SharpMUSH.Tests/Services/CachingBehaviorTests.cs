using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
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

		var result1 = await mediator.Send(new GetObjectNodeQuery(dbRef));

		// Verify cache key exists. GetObjectNodeQuery delegates the cached load to the number-keyed
		// GetObjectNodeByNumberQuery, so the entry lives under the number-only CacheKeys.Object key.
		var cacheKey = SharpMUSH.Library.Definitions.CacheKeys.Object(dbRef);
		var cached = await Cache.TryGetAsync<AnyOptionalSharpObject>(cacheKey);
		await Assert.That(cached.HasValue).IsTrue();

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
		var cacheKey = SharpMUSH.Library.Definitions.CacheKeys.Contents(dbRef);
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

		var before = await mediator.Send(new GetObjectNodeQuery(dbRef));
		await Assert.That(before.Object()!.Name).IsEqualTo("CacheInvalidation Test Object");

		// Rename via command — SetNameCommand invalidates object:{dbRef} cache key
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@name {dbRef}=CacheInvalidation Renamed Object"));

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

		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@create CacheInvalidation Visibility Test"));
		var newDbRef = Library.Models.DBRef.Parse(createResult.Message!.ToPlainText()!);

		var obj = await mediator.Send(new GetObjectNodeQuery(newDbRef));
		await Assert.That(obj.IsNone).IsFalse();
		await Assert.That(obj.Object()!.Name).IsEqualTo("CacheInvalidation Visibility Test");
	}


	/// <summary>
	/// Every object created in a room while that room's contents are being read must be in the room's
	/// contents afterwards. This is issue #838 through the real pipeline rather than at the behavior.
	/// </summary>
	/// <remarks>
	/// A stale contents entry does not heal: nothing re-invalidates the key, so the object stays missing
	/// from the room for the entry's whole lifetime and every look, locate and FOLLOW in that room is
	/// answered from it.
	/// </remarks>
	[Test]
	public async Task ContentsCache_HoldsEveryObjectCreatedWhileItWasBeingRead()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<Mediator.IMediator>();
		var options = WebAppFactory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

		var digResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@dig {TestIsolationHelpers.GenerateUniqueName("ContentsRace")}"));
		var room = Library.Models.DBRef.Parse(digResult.Message!.ToPlainText()!);

		async Task<Library.Models.DBRef> Populate() => await mediator.Send(new Library.Commands.Database.CreatePlayerCommand(
			TestIsolationHelpers.GenerateUniqueName("ContentsRacer"), "TestPassword123",
			room, room, (int)options.CurrentValue.Limit.StartingQuota));

		// The window is as wide as the read is slow, and a contents read costs one round trip per
		// occupant on Memgraph. An empty room is read faster than a creation commits and races nothing.
		for (var i = 0; i < 60; i++) await Populate();

		// One creation followed at once by a read of the room, which is what FOLLOW does: create the
		// leader, then resolve them by name out of the room's contents.
		using var readersRun = new CancellationTokenSource();
		var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
		{
			while (!readersRun.IsCancellationRequested)
			{
				await mediator.CreateStream(new GetContentsQuery(room)).ToListAsync();
			}
		})).ToArray();

		const int newcomers = 40;
		var missing = new List<Library.Models.DBRef>();
		for (var i = 0; i < newcomers; i++)
		{
			var newcomer = await Populate();
			var contents = await mediator.CreateStream(new GetContentsQuery(room)).ToListAsync();
			if (contents.All(c => c.Object().DBRef != newcomer)) missing.Add(newcomer);
		}

		await readersRun.CancelAsync();
		await Task.WhenAll(readers);

		await Assert.That(missing).IsEmpty()
			.Because($"a read that straddled a creation cached a list without it; {missing.Count} of {newcomers} are missing");
	}

	/// <summary>
	/// A read whose database query is issued <em>before</em> a write commits, but whose factory returns
	/// <em>after</em> that write's invalidation, must not leave its pre-write answer in the cache —
	/// whether the write names the entry by key or reaches it by tag.
	/// </summary>
	/// <remarks>
	/// Issue #838. <c>IFusionCache.RemoveAsync</c> drops only what is in the cache at that instant, so a
	/// straddling read stores its stale list on top of the invalidation and every later reader is served
	/// it until something else happens to clear the key. That is why a player created in a room could be
	/// absent from the room's cached contents indefinitely, and why <c>FOLLOW</c> answered "I can't see
	/// that here." for someone standing in the same room. <c>CreatePlayerCommand</c> invalidates
	/// <c>object-contents:#N</c> by key and declares no tag that covers it, so it was exactly the
	/// vulnerable shape; <c>MoveObjectCommand</c> escaped it only by falling back to the
	/// <c>ObjectContents</c> tag.
	/// <para>
	/// The window is as wide as the read is slow, which is why this surfaced on Memgraph — whose
	/// <c>GetContentsAsync</c> issues a query per item — and not on ArangoDB or SurrealDB.
	/// </para>
	/// </remarks>
	[Test]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task StraddlingRead_DoesNotOutliveTheWriteThatInvalidatedIt(bool byKey, bool byTag)
	{
		using var cache = new FusionCache(new FusionCacheOptions());
		var reads = new StreamQueryCachingBehavior<StaleReadProbe, string>(cache);
		var writes = new CacheInvalidationBehavior<StaleReadWrite, bool>(cache);
		var probe = new StaleReadProbe();
		var write = new StaleReadWrite(byKey ? [probe.CacheKey] : [], byTag ? probe.CacheTags : []);

		var readStarted = new TaskCompletionSource();
		var writeCommitted = new TaskCompletionSource();

		// A reader that queried the database before the write and has not stored its answer yet.
		var straddlingRead = Materialize(reads, probe, (_, _) => Blocked(readStarted, writeCommitted.Task, "before"));
		await readStarted.Task;

		await writes.Handle(write, (_, _) => ValueTask.FromResult(true), CancellationToken.None);
		writeCommitted.SetResult();

		await Assert.That(await straddlingRead).IsEquivalentTo(new[] { "before" })
			.Because("the straddling reader legitimately read a pre-write database; its own result is not the bug");

		var afterWrite = await Materialize(reads, probe, (_, _) => Once("after"));

		await Assert.That(afterWrite).IsEquivalentTo(new[] { "after" })
			.Because("the pre-write answer landed in the cache after the invalidation, so it must not be served");
	}

	private sealed record StaleReadProbe : IStreamQuery<string>, ICacheable
	{
		public string CacheKey => "stale-read-probe";
		public string[] CacheTags => ["stale-read-probe-tag"];
	}

	private sealed record StaleReadWrite(string[] CacheKeys, string[] CacheTags) : ICommand<bool>, ICacheInvalidating;

	private static async IAsyncEnumerable<string> Blocked(TaskCompletionSource started, Task release, string value)
	{
		started.SetResult();
		await release;
		yield return value;
	}

	private static async IAsyncEnumerable<string> Once(string value)
	{
		await Task.CompletedTask;
		yield return value;
	}

	private static async Task<List<string>> Materialize(
		StreamQueryCachingBehavior<StaleReadProbe, string> behavior,
		StaleReadProbe probe,
		StreamHandlerDelegate<StaleReadProbe, string> next)
	{
		var result = new List<string>();
		await foreach (var item in behavior.Handle(probe, next, CancellationToken.None))
		{
			result.Add(item);
		}

		return result;
	}
}
