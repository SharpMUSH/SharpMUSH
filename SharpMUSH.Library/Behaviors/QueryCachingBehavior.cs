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
		var cacheKey = $"{message.GetType().Name}:{message.GetHashCode()}";

		var tryGet = await cache.TryGetAsync<TResponse>(cacheKey, token: cancellationToken);
		if (tryGet.HasValue)
		{
			return tryGet.Value;
		}

		var response = await next(message, cancellationToken);
		await cache.SetAsync(cacheKey, response, _cacheDuration, token: cancellationToken);

		return response;
	}
}
