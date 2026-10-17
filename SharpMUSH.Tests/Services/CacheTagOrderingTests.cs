using Mediator;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

/// <summary>Schedules cache entry commits through IMemoryCache; no dependency internals are changed.</summary>
public class CacheTagOrderingTests
{
	private const string Tag = "counter-ordering";

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task AnOlderTagInvalidationCannotResurrectAnInvalidatedStream(bool overlapOlderInvalidation, bool scalar)
	{
		using var memory = new PausingMemoryCache();
		using var cache = new FusionCache(new FusionCacheOptions(), memory);
		var versions = new ObjectVersions();
		var reads = new StreamQueryCachingBehavior<CounterQuery, string>(cache, versions, Substitute.For<IMediator>());
		var writes = new CacheInvalidationBehavior<CounterWrite, bool>(cache, versions);
		var scalarReads = new QueryCachingBehavior<ScalarCounterQuery, string>(cache, versions, Substitute.For<IMediator>());
		var source = 49;
		var query = new CounterQuery();

		// Warm the global tag through normal reads, as fixture setup does in an actual world.
		await Read(new CounterQuery("warmup"), (_, _) => Once("irrelevant"));
		await Read(new CounterQuery("warmup"), (_, _) => Once("irrelevant"));

		Task<bool>? olderWrite = null;
		if (overlapOlderInvalidation)
		{
			// Pause the older command's FINAL invalidation. Its pre-handler invalidation remains intact.
			olderWrite = Task.Run(async () => await writes.Handle(new CounterWrite([]), (_, _) =>
			{
				memory.Arm();
				return ValueTask.FromResult(true);
			}, CancellationToken.None));
			await memory.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
		}

		var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finishRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var oldRead = Read(query, (_, _) => Capture());
		Task<List<string>>? joinedRead = null;
		await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
		try
		{
			await writes.Handle(new CounterWrite([query.CacheKey]), (_, _) =>
			{
				source = 50;
				return ValueTask.FromResult(true);
			}, CancellationToken.None);
		}
		finally
		{
			memory.Release.Set();
			if (olderWrite is not null) await olderWrite;
			joinedRead = Read(query, (_, _) => Once(source.ToString()));
			finishRead.SetResult();
		}

		await Assert.That(await oldRead).IsEquivalentTo(new[] { "49" })
			.Because("the original reader is entitled to the snapshot it actually read");
		var freshFactoryCalls = 0;
		var laterRead = await Read(query, (_, _) =>
		{
			freshFactoryCalls++;
			return Once(source.ToString());
		});
		await Assert.That(await joinedRead!).IsEquivalentTo(new[] { "50" })
			.Because("a caller starting after the write cannot inherit the old factory snapshot");
		await Assert.That(source).IsEqualTo(50);
		await Assert.That(laterRead).IsEquivalentTo(new[] { "50" })
			.Because($"an independent reader must not reuse the invalidated 49; source={source}, fresh factory calls={freshFactoryCalls}, tag generation={versions.OfTag(Tag)}");

		async Task<List<string>> Read(CounterQuery request, StreamHandlerDelegate<CounterQuery, string> next)
		{
			if (!scalar) return await reads.Handle(request, next, CancellationToken.None).ToListAsync();
			var value = await scalarReads.Handle(new ScalarCounterQuery(request.CacheKey),
				async (_, ct) => (await next(request, ct).ToListAsync(ct)).Single(), CancellationToken.None);
			return [value];
		}

		async IAsyncEnumerable<string> Capture()
		{
			var snapshot = source;
			captured.SetResult();
			await finishRead.Task;
			yield return snapshot.ToString();
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DynamicallyDiscoveredObjectTagsKeepTheFactoryStartEpoch(bool stream)
	{
		using var memory = new PausingMemoryCache(CacheKeys.ObjectTag(7));
		using var cache = new FusionCache(new FusionCacheOptions(), memory);
		await cache.GetOrSetAsync<string>("warm-object-tag", (_, _) => Task.FromResult("warm"), tags: [CacheKeys.ObjectTag(7)]);
		await cache.GetOrSetAsync<string>("warm-object-tag", (_, _) => Task.FromResult("warm"), tags: [CacheKeys.ObjectTag(7)]);
		var versions = new ObjectVersions();
		var factory = new TestObjectFactory();
		var old = factory.CreateThing(7, "before").AsOptionalContent.Expect<AnySharpContent>();
		var current = factory.CreateThing(8, "after").AsOptionalContent.Expect<AnySharpContent>();
		var mediator = Substitute.For<IMediator>();
		var scalarReads = new QueryCachingBehavior<ThingQuery, AnySharpContent>(cache, versions, mediator);
		var streamReads = new StreamQueryCachingBehavior<ThingStreamQuery, AnySharpContent>(cache, versions, mediator);
		var writes = new CacheInvalidationBehavior<ObjectWrite, bool>(cache, versions);
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var olderWrite = Task.Run(async () => await writes.Handle(new ObjectWrite(7), (_, _) =>
		{
			memory.Arm();
			return ValueTask.FromResult(true);
		}, CancellationToken.None));
		await memory.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
		var first = Read(true);
		await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
		// Only an object KEY is invalidated; dynamic object tags do not use BumpTag.
		try
		{
			await writes.Handle(new ObjectWrite(7), (_, _) => ValueTask.FromResult(true), CancellationToken.None);
		}
		finally
		{
			memory.Release.Set();
			await olderWrite;
			release.SetResult();
		}
		await first;
		var later = await Read(false);
		await Assert.That(later.Single().Object().DBRef.Number).IsEqualTo(8);
		await mediator.DidNotReceive().Send(Arg.Is<SharpMUSH.Library.Queries.Database.GetObjectNodeQuery>(q => q.DBRef.Number == 7), Arg.Any<CancellationToken>());

		async Task<List<AnySharpContent>> Read(bool blocked)
		{
			if (stream) return await streamReads.Handle(new ThingStreamQuery(), (_, _) => Values(), CancellationToken.None).ToListAsync();
			return [await scalarReads.Handle(new ThingQuery(), async (_, _) => (await Values().ToListAsync()).Single(), CancellationToken.None)];
			async IAsyncEnumerable<AnySharpContent> Values()
			{
				if (blocked)
				{
					started.SetResult();
					await release.Task;
				}
				yield return blocked ? old : current;
			}
		}
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task AStaleCertificateForcesExactlyOneUncachedRead(int shape)
	{
		using var cache = new FusionCache(new FusionCacheOptions());
		var versions = new ObjectVersions();
		var mediator = Substitute.For<IMediator>();
		var calls = 0;
		var current = new TestObjectFactory().CreateThing(8, "current").AsOptionalContent.Expect<AnySharpContent>();
		var key = shape switch { 0 => "scalar", 1 => "dynamic-thing", 2 => "counter", _ => "dynamic-things" };
		var options = CacheEntryProfiles.Tagged;
		switch (shape)
		{
			case 0:
				await cache.SetAsync(key, new CachedValue<string>("49", versions.Epoch, [Tag]), options);
				break;
			case 1:
				await cache.SetAsync(key, new CachedValue<CachedObjectRef>(new CachedObjectRef(new DBRef(7)), versions.Epoch, [CacheKeys.ObjectTag(7)]), options);
				break;
			case 2:
				await cache.SetAsync(key, new CachedValue<List<string>>(["49"], versions.Epoch, [Tag]), options);
				break;
			default:
				await cache.SetAsync(key, new CachedValue<CachedObjectRefs>(new CachedObjectRefs([new DBRef(7)]), versions.Epoch, [CacheKeys.ObjectTag(7)]), options);
				break;
		}
		versions.BumpTag(Tag);
		versions.Bump(7);
		switch (shape)
		{
			case 0:
				var scalar = new QueryCachingBehavior<ScalarCounterQuery, string>(cache, versions, mediator);
				var scalarResult = await scalar.Handle(new ScalarCounterQuery(key), (_, _) => { calls++; return ValueTask.FromResult("50"); }, CancellationToken.None);
				await Assert.That(scalarResult).IsEqualTo("50");
				await Assert.That((await cache.TryGetAsync<CachedValue<string>>(key)).HasValue).IsFalse();
				break;
			case 1:
				var shaped = new QueryCachingBehavior<ThingQuery, AnySharpContent>(cache, versions, mediator);
				var shapedResult = await shaped.Handle(new ThingQuery(), (_, _) => { calls++; return ValueTask.FromResult(current); }, CancellationToken.None);
				await Assert.That(shapedResult.Object().DBRef.Number).IsEqualTo(8);
				await Assert.That((await cache.TryGetAsync<CachedValue<CachedObjectRef>>(key)).HasValue).IsFalse();
				break;
			case 2:
				var stream = new StreamQueryCachingBehavior<CounterQuery, string>(cache, versions, mediator);
				var streamResult = await stream.Handle(new CounterQuery(), (_, _) => { calls++; return Once("50"); }, CancellationToken.None).ToListAsync();
				await Assert.That(streamResult).IsEquivalentTo(new[] { "50" });
				await Assert.That((await cache.TryGetAsync<CachedValue<List<string>>>(key)).HasValue).IsFalse();
				break;
			default:
				var shapedStream = new StreamQueryCachingBehavior<ThingStreamQuery, AnySharpContent>(cache, versions, mediator);
				var shapedStreamResult = await shapedStream.Handle(new ThingStreamQuery(), (_, _) => { calls++; return new[] { current }.ToAsyncEnumerable(); }, CancellationToken.None).ToListAsync();
				await Assert.That(shapedStreamResult.Single().Object().DBRef.Number).IsEqualTo(8);
				await Assert.That((await cache.TryGetAsync<CachedValue<CachedObjectRefs>>(key)).HasValue).IsFalse();
				break;
		}
		await Assert.That(calls).IsEqualTo(1);
		await mediator.DidNotReceive().Send(Arg.Any<SharpMUSH.Library.Queries.Database.GetObjectNodeQuery>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task AnUnrelatedTagChangePreservesAValidHit()
	{
		using var cache = new FusionCache(new FusionCacheOptions());
		var versions = new ObjectVersions();
		var reads = new QueryCachingBehavior<ScalarCounterQuery, string>(cache, versions, Substitute.For<IMediator>());
		await reads.Handle(new ScalarCounterQuery("unrelated"), (_, _) => ValueTask.FromResult("kept"), CancellationToken.None);
		versions.BumpTag("unrelated-tag");
		var result = await reads.Handle(new ScalarCounterQuery("unrelated"), (_, _) => throw new InvalidOperationException("must be a hit"), CancellationToken.None);
		await Assert.That(result).IsEqualTo("kept");
	}

	[Test]
	public async Task ConcurrentInvalidationEpochsNeverRegress()
	{
		var versions = new ObjectVersions();
		await Parallel.ForAsync(0, 2048, async (_, _) =>
		{
			versions.Bump(7);
			versions.BumpTag(Tag);
			await Task.CompletedTask;
		});
		await Assert.That(versions.Of(7)).IsEqualTo(2048);
		await Assert.That(versions.OfTag(Tag)).IsEqualTo(2048);
		await Assert.That(versions.ChangedAfter(Tag, versions.Epoch - 1)).IsTrue()
			.Because("the last allocated epoch must remain present after all older concurrent updates finish");
	}

	private sealed record ScalarCounterQuery(string CacheKey) : IQuery<string>, ICacheable
	{
		public string[] CacheTags => [Tag];
	}
	private sealed record ThingQuery : IQuery<AnySharpContent>, ICacheable
	{
		public string CacheKey => "dynamic-thing";
		public string[] CacheTags => [];
	}
	private sealed record ThingStreamQuery : IStreamQuery<AnySharpContent>, ICacheable
	{
		public string CacheKey => "dynamic-things";
		public string[] CacheTags => [];
	}
	private sealed record ObjectWrite(int Number) : ICommand<bool>, ICacheInvalidating
	{
		public string[] CacheKeys => [SharpMUSH.Library.Definitions.CacheKeys.Object(Number)];
		public string[] CacheTags => [];
	}

	private static async IAsyncEnumerable<string> Once(string value)
	{
		await Task.CompletedTask;
		yield return value;
	}

	private sealed record CounterQuery(string CacheKey = "counter") : IStreamQuery<string>, ICacheable
	{
		public string[] CacheTags => [Tag];
	}

	private sealed record CounterWrite(string[] CacheKeys) : ICommand<bool>, ICacheInvalidating
	{
		public string[] CacheTags => [Tag];
	}

	private sealed class PausingMemoryCache(string pausedTag = Tag) : IMemoryCache
	{
		private readonly string _pausedTag = pausedTag;
		private readonly MemoryCache _inner = new(new MemoryCacheOptions());
		private int _armed;
		public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public ManualResetEventSlim Release { get; } = new();
		public void Arm() => Interlocked.Exchange(ref _armed, 1);
		public ICacheEntry CreateEntry(object key) => new PausingEntry(_inner.CreateEntry(key), this);
		public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);
		public void Remove(object key) => _inner.Remove(key);
		public void Dispose()
		{
			_inner.Dispose();
			Release.Dispose();
		}

		private sealed class PausingEntry(ICacheEntry inner, PausingMemoryCache owner) : ICacheEntry
		{
			public object Key => inner.Key;
			public object? Value { get => inner.Value; set => inner.Value = value; }
			public DateTimeOffset? AbsoluteExpiration { get => inner.AbsoluteExpiration; set => inner.AbsoluteExpiration = value; }
			public TimeSpan? AbsoluteExpirationRelativeToNow { get => inner.AbsoluteExpirationRelativeToNow; set => inner.AbsoluteExpirationRelativeToNow = value; }
			public TimeSpan? SlidingExpiration { get => inner.SlidingExpiration; set => inner.SlidingExpiration = value; }
			public IList<IChangeToken> ExpirationTokens => inner.ExpirationTokens;
			public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => inner.PostEvictionCallbacks;
			public CacheItemPriority Priority { get => inner.Priority; set => inner.Priority = value; }
			public long? Size { get => inner.Size; set => inner.Size = value; }
			public void Dispose()
			{
				// The key is exposed by the public IMemoryCache entry API. All values and operations pass through.
				if (Key.ToString() == "__fc:t:" + owner._pausedTag && Interlocked.CompareExchange(ref owner._armed, 0, 1) == 1)
				{
					owner.Paused.SetResult();
					if (!owner.Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Tag commit was not released");
				}
				inner.Dispose();
			}
		}
	}
}
