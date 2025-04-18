using System.Diagnostics;
using Mediator;
using SharpMUSH.Library.Attributes;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

public class QueryCachingBehavior<TRequest, TResponse>(IFusionCache cache)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IQuery<TResponse>, ICacheable
{
	private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

	public async ValueTask<TResponse> Handle(
		TRequest message,
		CancellationToken cancellationToken,
		MessageHandlerDelegate<TRequest, TResponse> next
	)
	{
		return message.CacheTags.Length > 0
			? await cache.GetOrSetAsync(message.CacheKey, await next(message, cancellationToken),
				tags: message.CacheTags, token: cancellationToken)
			: await cache.GetOrSetAsync(message.CacheKey, await next(message, cancellationToken),
				token: cancellationToken);
	}
}