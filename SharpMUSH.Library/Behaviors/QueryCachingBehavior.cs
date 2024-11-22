using MediatR;
using Microsoft.Extensions.Caching.Memory;
using System.Reflection;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Behaviors;

public class QueryCachingBehavior<TRequest, TResponse>(IMemoryCache cache) : IPipelineBehavior<TRequest, TResponse>
	where TRequest : IRequest<TResponse>
{
	private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var cacheAttribute = request.GetType().GetCustomAttribute<CacheableQueryAttribute>();
        if (cacheAttribute == null) return await next();

        var cacheKey = $"{request.GetType().Name}:{request.GetHashCode()}";
        
        if (cache.TryGetValue(cacheKey, out TResponse? cachedResponse))
            return cachedResponse!;

        var response = await next();
        cache.Set(cacheKey, response, _cacheDuration);
        
        return response;
    }
}