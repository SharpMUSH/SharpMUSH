using Mediator;
using SharpMUSH.Library.Attributes;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

public class CacheInvalidationBehavior<TRequest, TResponse>(IFusionCache cache) : IPipelineBehavior<TRequest, TResponse>
	where TRequest : ICommand<TResponse>, ICacheInvalidating
{
	public async ValueTask<TResponse> Handle(TRequest message,
		MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		await InvalidateCacheAsync(message, cancellationToken);

		var result = await next(message, cancellationToken);

		// Invalidate again after the handler completes to clear any cache entries
		// that were repopulated by concurrent reads during the handler execution.
		await InvalidateCacheAsync(message, cancellationToken);

		return result;
	}

	private async ValueTask InvalidateCacheAsync(TRequest message, CancellationToken cancellationToken)
	{
		foreach (var key in message.CacheKeys)
		{
			await cache.RemoveAsync(key, token: cancellationToken);
		}

		if (message.CacheTags.Length != 0)
		{
			await cache.RemoveByTagAsync(message.CacheTags, token: cancellationToken);
		}
	}
}