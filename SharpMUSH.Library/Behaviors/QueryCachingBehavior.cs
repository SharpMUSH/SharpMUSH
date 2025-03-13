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
		/*try
		{
			if (message.CacheTags.Length > 0)
			{
				return await cache.GetOrSetAsync(message.CacheKey, await next(message, cancellationToken),
					tags: message.CacheTags, token: cancellationToken);
			}

			return await cache.GetOrSetAsync(message.CacheKey, await next(message, cancellationToken),
				token: cancellationToken);
		}
		catch (Exception e)
		{
			Debugger.Break();
			System.Console.WriteLine("BEEEEE");
			throw;
		}*/
		return await next(message, cancellationToken);
	}
}