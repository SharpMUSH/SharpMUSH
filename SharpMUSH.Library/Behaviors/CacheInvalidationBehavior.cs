using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace SharpMUSH.Library.Behaviors;

public class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IMemoryCache _cache;

    public CacheInvalidationBehavior(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        // If this is a command (not a query), invalidate relevant cache entries
        if (request.GetType().Namespace?.Contains("Commands") == true)
        {
            // Create a pattern for cache keys to invalidate based on the command type
            var keyPattern = request.GetType().Name.Replace("Command", "");
            
            // For memory cache, we'd need to track keys separately to implement pattern-based invalidation
            // This is a simplified version that removes exact matches
            if (_cache is MemoryCache memoryCache)
            {
                // Remove any cached queries that might be affected by this command
                var cacheKeys = memoryCache.GetKeys<string>().Where(k => k.Contains(keyPattern));
                foreach (var key in cacheKeys)
                {
                    _cache.Remove(key);
                }
            }
        }

        return response;
    }
}

// Extension method to get cache keys (since MemoryCache doesn't expose them directly)
public static class MemoryCacheExtensions
{
    public static IEnumerable<T> GetKeys<T>(this MemoryCache cache)
    {
        var field = typeof(MemoryCache).GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var entries = field?.GetValue(cache) as IDictionary<object, object>;
        
        return entries?.Keys.OfType<T>() ?? Enumerable.Empty<T>();
    }
}