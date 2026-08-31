using Mediator;
using SharpMUSH.Library.Attributes;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

public class QueryCachingBehavior<TRequest, TResponse>(IFusionCache cache, ICacheInvalidationClock clock)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IQuery<TResponse>, ICacheable
{
	public async ValueTask<TResponse> Handle(
		TRequest message,
		MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken
	)
	{
		// Taken before the call, not just before the factory: FusionCache lets a second caller join a
		// factory that is already running, so this call can be handed an answer read before it even began.
		var callStartedAt = clock.Now();
		var readItself = false;

		var response = await cache.GetOrSetAsync<TResponse>(message.CacheKey,
			async (ctx, ct) =>
			{
				readItself = true;
				// Before the handler runs, so it covers the whole read and not just its tail.
				var readStartedAt = clock.Now();
				var response = await next(message, ct);

				CacheStalenessGuard.SkipWriteIfInvalidated(ctx, clock, message.CacheKey, readStartedAt);

				return response;
			},
			options: null,
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);

		// Declining to store a stale answer keeps it away from later readers, but not from this one. A
		// call that ran its own factory read after it began, which is as fresh as its caller can ask for.
		// A call that joined somebody else's factory did not: it is handed an answer read before it
		// started. Read once more for that case only -- the second read begins after the invalidation was
		// recorded, so it sees the write.
		return !readItself && clock.InvalidatedSince(message.CacheKey, callStartedAt)
			? await next(message, cancellationToken)
			: response;
	}
}
