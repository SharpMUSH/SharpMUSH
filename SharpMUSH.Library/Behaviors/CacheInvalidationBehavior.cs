using Mediator;
using Microsoft.Extensions.Caching.Memory;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Behaviors;

public class CacheInvalidationBehavior<TRequest, TResponse>(IMemoryCache cache) : IPipelineBehavior<TRequest, TResponse>
	where TRequest : IRequest<TResponse>, ICacheable
{
		public async ValueTask<TResponse> Handle(
		TRequest message,
		CancellationToken cancellationToken,
		MessageHandlerDelegate<TRequest, TResponse> next
	)
	{
		var response = await next(message, cancellationToken);

		/*
		// If this is a command (not a query), invalidate relevant cache entries
		if (message.GetType().Namespace?.Contains("Commands") == true)
		{
			// Create a pattern for cache keys to invalidate based on the command type
			var keyPattern = message.GetType().Name.Replace("Command", "");

			// For memory cache, we'd need to track keys separately to implement pattern-based invalidation
			// This is a simplified version that removes exact matches
			if (cache is MemoryCache memoryCache)
			{
				// Remove any cached queries that might be affected by this command
				var cacheKeys = memoryCache.GetKeys<string>().Where(k => k.Contains(keyPattern));
				foreach (var key in cacheKeys)
				{
					cache.Remove(key);
				}
			}
		}
		*/
		return response;
	}
}

// Extension method to get cache keys (since MemoryCache doesn't expose them directly)
public static class MemoryCacheExtensions
{
	public static IEnumerable<T> GetKeys<T>(this MemoryCache cache)
	{
		var field = typeof(MemoryCache).GetField(
			"_entries",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
		);
		var entries = field?.GetValue(cache) as IDictionary<object, object>;

		return entries?.Keys.OfType<T>() ?? Enumerable.Empty<T>();
	}
}
