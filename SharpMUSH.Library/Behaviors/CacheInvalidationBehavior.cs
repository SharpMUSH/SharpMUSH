using Mediator;
using MoreLinq;
using SharpMUSH.Library.Attributes;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

public class CacheInvalidationBehavior<TRequest, TResponse>(IFusionCache cache) : IPipelineBehavior<TRequest, TResponse>
	where TRequest : ICommand<TResponse>, ICacheInvalidating
{
	public async ValueTask<TResponse> Handle(
		TRequest message,
		CancellationToken cancellationToken,
		MessageHandlerDelegate<TRequest, TResponse> next
	)
	{
		await foreach(var key in message.CacheKeys.ToAsyncEnumerable().WithCancellation(cancellationToken))
		{
			await cache.RemoveAsync(key, token: cancellationToken);
		}
		
		await cache.RemoveByTagAsync(message.CacheTags, token: cancellationToken);
		
		return await next(message, cancellationToken);
	}
}