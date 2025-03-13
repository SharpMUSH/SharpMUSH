using Mediator;
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
		await cache.ClearAsync(token: cancellationToken);
		
		// TODO: Use Cache 'tags' in order to better query and clear items.
		return await next(message, cancellationToken);
	}
}