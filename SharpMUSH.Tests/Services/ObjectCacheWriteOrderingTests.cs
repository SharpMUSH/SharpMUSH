using System.Reflection;
using System.Runtime.ExceptionServices;
using Mediator;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

public class ObjectCacheWriteOrderingTests
{
	private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

	[Test]
	public async Task APostWriteObjectReaderCannotInheritAPreWriteFactorySnapshot()
	{
		using var memory = new MemoryCache(new MemoryCacheOptions());
		using var realCache = new FusionCache(new FusionCacheOptions(), memory);
		var cache = DispatchProxy.Create<IFusionCache, HoldFirstFactoryReturn>();
		var hold = (HoldFirstFactoryReturn)(object)cache;
		hold.Inner = realCache;
		var versions = new ObjectVersions();
		var reads = new QueryCachingBehavior<GetObjectNodeByNumberQuery, AnyOptionalSharpObject>(cache, versions, Substitute.For<IMediator>());
		var writes = new CacheInvalidationBehavior<ObjectWrite, bool>(cache, versions);
		var objects = new TestObjectFactory();
		AnyOptionalSharpObject current = objects.CreateThing(7, "before");
		var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finishFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new CancellationTokenSource();
		var first = reads.Handle(new GetObjectNodeByNumberQuery(7), async (_, token) =>
		{
			var snapshot = current;
			captured.TrySetResult();
			await finishFactory.Task.WaitAsync(WaitLimit, token);
			return snapshot;
		}, cancellation.Token).AsTask();
		Task<AnyOptionalSharpObject>? joined = null;
		try
		{
			await captured.Task.WaitAsync(WaitLimit);
			await writes.Handle(new ObjectWrite(7), (_, _) =>
			{
				current = new TestObjectFactory().CreateThing(7, "after");
				return ValueTask.FromResult(true);
			}, cancellation.Token);
			await Assert.That(current.Object()!.Name).IsEqualTo("after");

			// This call reaches the real cache while its original factory is still held, but AFTER
			// both invalidation passes completed. It is entitled to the committed name, not "before".
			joined = reads.Handle(new GetObjectNodeByNumberQuery(7), (_, _) => ValueTask.FromResult(current), cancellation.Token).AsTask();
			finishFactory.TrySetResult();
			await hold.FactoryReturned.Task.WaitAsync(WaitLimit);

			// Hold only the first caller's return from GetOrSetAsync: the real factory has stored its
			// answer and released its key lock. This makes the joining caller finish before the first
			// caller's version-check cleanup, instead of relying on continuation scheduling luck.
			var result = await joined.WaitAsync(WaitLimit);
			await Assert.That(result.Object()!.Name).IsEqualTo("after")
				.Because("this reader started after the write completed, even though it joined an older factory");
		}
		finally
		{
			finishFactory.TrySetResult();
			hold.Release.TrySetResult();
			await cancellation.CancelAsync();
			await Task.WhenAll(first, joined ?? Task.FromResult(current)).WaitAsync(WaitLimit);
		}

		await Assert.That((await first).Object()!.Name).IsEqualTo("before")
			.Because("the original reader is allowed the database snapshot it read before the write");
	}

	[Test]
	public async Task AStaleObjectCertificateReadsTheHandlerExactlyOnce()
	{
		using var memory = new MemoryCache(new MemoryCacheOptions());
		using var cache = new FusionCache(new FusionCacheOptions(), memory);
		var versions = new ObjectVersions();
		var objects = new TestObjectFactory();
		AnyOptionalSharpObject before = objects.CreateThing(7, "before");
		AnyOptionalSharpObject after = new TestObjectFactory().CreateThing(7, "after");
		var query = new GetObjectNodeByNumberQuery(7);
		await cache.SetAsync(query.CacheKey,
			new CachedValue<AnyOptionalSharpObject>(before, versions.Epoch, [CacheKeys.ObjectTag(7)]), CacheEntryProfiles.Object);
		versions.Bump(7);
		var reads = new QueryCachingBehavior<GetObjectNodeByNumberQuery, AnyOptionalSharpObject>(cache, versions, Substitute.For<IMediator>());
		var calls = 0;

		var result = await reads.Handle(query, (_, _) =>
		{
			calls++;
			return ValueTask.FromResult(after);
		}, CancellationToken.None);

		await Assert.That(result.Object()!.Name).IsEqualTo("after");
		await Assert.That(ReferenceEquals(result.Object(), after.Object())).IsTrue();
		await Assert.That(calls).IsEqualTo(1);
		await Assert.That((await cache.TryGetAsync<CachedValue<AnyOptionalSharpObject>>(query.CacheKey)).HasValue).IsFalse()
			.Because("the stale entry is removed and the fallback is one direct handler read, not another cached factory");
	}

	[Test]
	public async Task AnUnrelatedObjectWritePreservesTheCachedInstance()
	{
		using var memory = new MemoryCache(new MemoryCacheOptions());
		using var cache = new FusionCache(new FusionCacheOptions(), memory);
		var versions = new ObjectVersions();
		var reads = new QueryCachingBehavior<GetObjectNodeByNumberQuery, AnyOptionalSharpObject>(cache, versions, Substitute.For<IMediator>());
		var writes = new CacheInvalidationBehavior<ObjectWrite, bool>(cache, versions);
		AnyOptionalSharpObject original = new TestObjectFactory().CreateThing(7, "unchanged");
		var query = new GetObjectNodeByNumberQuery(7);
		var calls = 0;
		var first = await reads.Handle(query, (_, _) =>
		{
			calls++;
			return ValueTask.FromResult(original);
		}, CancellationToken.None);
		var certificate = (await cache.TryGetAsync<CachedValue<AnyOptionalSharpObject>>(query.CacheKey)).Value;

		await writes.Handle(new ObjectWrite(8), (_, _) => ValueTask.FromResult(true), CancellationToken.None);
		await Assert.That(versions.Epoch).IsGreaterThan(certificate.Epoch);
		var second = await reads.Handle(query, (_, _) =>
		{
			calls++;
			return ValueTask.FromResult<AnyOptionalSharpObject>(new TestObjectFactory().CreateThing(7, "unexpected reload"));
		}, CancellationToken.None);

		await Assert.That(calls).IsEqualTo(1);
		await Assert.That(ReferenceEquals(first.Object(), second.Object())).IsTrue();
		await Assert.That(ReferenceEquals(certificate,
			(await cache.TryGetAsync<CachedValue<AnyOptionalSharpObject>>(query.CacheKey)).Value)).IsTrue();
	}

	[Test]
	public async Task AStaleObjectFallbackObservesCallerCancellation()
	{
		using var memory = new MemoryCache(new MemoryCacheOptions());
		using var cache = new FusionCache(new FusionCacheOptions(), memory);
		var versions = new ObjectVersions();
		var query = new GetObjectNodeByNumberQuery(7);
		AnyOptionalSharpObject old = new TestObjectFactory().CreateThing(7, "before");
		await cache.SetAsync(query.CacheKey,
			new CachedValue<AnyOptionalSharpObject>(old, versions.Epoch, [CacheKeys.ObjectTag(7)]), CacheEntryProfiles.Object);
		versions.Bump(7);
		var reads = new QueryCachingBehavior<GetObjectNodeByNumberQuery, AnyOptionalSharpObject>(cache, versions, Substitute.For<IMediator>());
		using var cancellation = new CancellationTokenSource();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken observed = default;
		var calls = 0;
		var pending = reads.Handle(query, async (_, token) =>
		{
			calls++;
			observed = token;
			entered.TrySetResult();
			await release.Task.WaitAsync(WaitLimit, token);
			return old;
		}, cancellation.Token).AsTask();
		var cancelled = false;
		try
		{
			await entered.Task.WaitAsync(WaitLimit);
			await cancellation.CancelAsync();
			try { await pending.WaitAsync(WaitLimit); }
			catch (OperationCanceledException) { cancelled = true; }
		}
		finally
		{
			await cancellation.CancelAsync();
			release.TrySetResult();
			try { await pending.WaitAsync(WaitLimit); }
			catch (OperationCanceledException) { }
		}

		await Assert.That(cancelled).IsTrue();
		await Assert.That(observed).IsEqualTo(cancellation.Token);
		await Assert.That(calls).IsEqualTo(1);
		await Assert.That((await cache.TryGetAsync<CachedValue<AnyOptionalSharpObject>>(query.CacheKey)).HasValue).IsFalse();
	}

	private sealed record ObjectWrite(int Number) : ICommand<bool>, ICacheInvalidating
	{
		public string[] CacheKeys => [SharpMUSH.Library.Definitions.CacheKeys.Object(Number)];
		public string[] CacheTags => [];
	}

	/// <summary>
	/// Forwards every operation to real FusionCache. Only delivery of the first GetOrSetAsync result
	/// is paused, after FusionCache has completed it; cache entries, locks and invalidation are real.
	/// </summary>
	public class HoldFirstFactoryReturn : DispatchProxy
	{
		public IFusionCache Inner { get; set; } = null!;
		public TaskCompletionSource FactoryReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _seen;

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			try
			{
				var result = targetMethod!.Invoke(Inner, args);
				if (targetMethod.Name == nameof(IFusionCache.GetOrSetAsync)
					&& Interlocked.Increment(ref _seen) == 1)
				{
					return typeof(HoldFirstFactoryReturn).GetMethod(nameof(Hold), BindingFlags.Instance | BindingFlags.NonPublic)!
						.MakeGenericMethod(targetMethod.ReturnType.GetGenericArguments()[0]).Invoke(this, [result]);
				}
				return result;
			}
			catch (TargetInvocationException exception) when (exception.InnerException is not null)
			{
				ExceptionDispatchInfo.Throw(exception.InnerException);
				throw;
			}
		}

		private async ValueTask<T> Hold<T>(ValueTask<T> pending)
		{
			var value = await pending;
			FactoryReturned.TrySetResult();
			await Release.Task.WaitAsync(WaitLimit);
			return value;
		}
	}
}
