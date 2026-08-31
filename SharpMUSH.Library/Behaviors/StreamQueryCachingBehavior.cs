using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library.Attributes;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Caches <see cref="IStreamQuery{TResponse}"/> results that implement <see cref="ICacheable"/>.
/// On cache miss the stream is materialized to a list, stored in FusionCache, and yielded.
/// On cache hit the stored list is yielded directly.
/// </summary>
public class StreamQueryCachingBehavior<TRequest, TResponse>(IFusionCache cache, ICacheInvalidationClock clock)
	: IStreamPipelineBehavior<TRequest, TResponse>
	where TRequest : IStreamQuery<TResponse>, ICacheable
{
	public async IAsyncEnumerable<TResponse> Handle(
		TRequest message,
		StreamHandlerDelegate<TRequest, TResponse> next,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		// Taken before the call, not just before the factory: FusionCache lets a second caller join a
		// factory that is already running, so this call can be handed an answer read before it even began.
		var callStartedAt = clock.Now();
		var readItself = false;

		var list = await cache.GetOrSetAsync<List<TResponse>>(message.CacheKey,
			async (ctx, ct) =>
			{
				readItself = true;
				// Before the stream is drawn on, so it covers the whole read and not just its tail.
				var readStartedAt = clock.Now();
				var materialized = await MaterializeAsync(message, next, ct);

				CacheStalenessGuard.SkipWriteIfInvalidated(ctx, clock, message.CacheKey, readStartedAt);

				return materialized;
			},
			options: null,
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);

		// Declining to store a stale answer keeps it away from later readers, but not from this one. A
		// call that ran its own factory read after it began, which is as fresh as its caller can ask for.
		// A call that joined somebody else's factory did not: it is handed a list read before it started,
		// and if a write landed in between, that list is missing it. Read once more for that case only --
		// the second read begins after the invalidation was recorded, so it sees the write.
		if (!readItself && clock.InvalidatedSince(message.CacheKey, callStartedAt))
		{
			list = await MaterializeAsync(message, next, cancellationToken);
		}

		foreach (var item in list)
		{
			yield return item;
		}
	}

	private static async ValueTask<List<TResponse>> MaterializeAsync(
		TRequest message,
		StreamHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		var result = new List<TResponse>();
		await foreach (var item in next(message, cancellationToken).WithCancellation(cancellationToken))
		{
			result.Add(item);
		}
		return result;
	}
}
