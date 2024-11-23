using System.Reflection;
using Mediator;
using Microsoft.Extensions.Caching.Memory;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Behaviors;

public class QueryCachingBehavior<TRequest, TResponse>(IMemoryCache cache)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IRequest<TResponse>
{
	private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

	public async ValueTask<TResponse> Handle(
		TRequest message,
		CancellationToken cancellationToken,
		MessageHandlerDelegate<TRequest, TResponse> next
	)
	{
		var cacheAttribute = message.GetType().GetCustomAttribute<CacheableQueryAttribute>();
		if (cacheAttribute == null)
			return await next(message, cancellationToken);

		var cacheKey = $"{message.GetType().Name}:{message.GetHashCode()}";

		if (cache.TryGetValue(cacheKey, out TResponse? cachedResponse))
			return cachedResponse!;

		var response = await next(message, cancellationToken);
		cache.Set(cacheKey, response, _cacheDuration);

		return response;
	}
}
