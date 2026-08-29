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

				// The caller still gets what the database said; it is only keeping it that is wrong.
				if (clock.InvalidatedSince(message.CacheKey, readStartedAt))
				{
					// Both layers: memory is the only one configured today, but a distributed cache added
					// later would otherwise be handed exactly the answer we are refusing to keep. Nothing
					// was written, so there is nothing for the backplane to announce either.
					ctx.Options
						.SetSkipMemoryCacheWrite(true)
						.SetSkipDistributedCacheWrite(true, skipBackplaneNotifications: true);
				}

				return response;
			},
			options: null,
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);
}
