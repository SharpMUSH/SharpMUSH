using Mediator;
using SharpMUSH.Library.Attributes;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

public class QueryCachingBehavior<TRequest, TResponse>(IFusionCache cache)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IQuery<TResponse>, ICacheable
{
	public async ValueTask<TResponse> Handle(
		TRequest message,
		MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken
	)
		=> await cache.GetOrSetAsync(message.CacheKey,
			async _ => await next(message, cancellationToken),
			tags: CacheEntryTags.For(message), token: cancellationToken);
}
