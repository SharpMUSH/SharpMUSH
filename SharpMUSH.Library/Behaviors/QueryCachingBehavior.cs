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
		=> await cache.GetOrSetAsync<TResponse>(message.CacheKey,
			async (ctx, ct) =>
			{
				// Before the handler runs, so it covers the whole read and not just its tail.
				var readStartedAt = clock.Now();
				var response = await next(message, ct);

				CacheStalenessGuard.SkipWriteIfInvalidated(ctx, clock, message.CacheKey, readStartedAt);

				return response;
			},
			options: null,
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);
}
