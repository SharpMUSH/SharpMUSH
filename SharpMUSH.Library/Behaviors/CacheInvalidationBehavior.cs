using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Drops a write's cache entries on both sides of its handler.
/// </summary>
/// <remarks>
/// The pass <em>after</em> the handler is the one that matters, and it used to be opt-in: production
/// invalidated only before the write ran. That is backwards for a read-through cache. Between the
/// invalidation and the write's commit there is a window in which any concurrent read repopulates the
/// key from the pre-write database, and nothing invalidates it again — so the stale entry outlives the
/// write for the entry's whole lifetime. Issue #797 is that shape: an object whose location edge says
/// <c>#0</c> while <c>#0</c>'s cached contents list, materialised in that window, does not contain it,
/// and does not recover on retry.
/// <para>
/// The tests turned the second pass on and production did not, which meant this class was never
/// exercised in the configuration it shipped in. Both passes now always run.
/// </para>
/// <para>
/// A narrower window remains and is not closed here: a read that issues its query before the commit
/// and stores its result after the second invalidation still caches a pre-write answer. Closing that
/// needs the read side to carry a version, not more invalidation.
/// </para>
/// <para>
/// The second pass runs on the failure path too, and under <see cref="CancellationToken.None"/>. A
/// handler that threw may have committed part of its write first, and a cancelled caller may have
/// left one behind entirely; in both cases the entry is stale and nobody else is going to clear it.
/// </para>
/// </remarks>
public class CacheInvalidationBehavior<TRequest, TResponse>(IFusionCache cache)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : ICommand<TResponse>, ICacheInvalidating
{
	public async ValueTask<TResponse> Handle(TRequest message,
		MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		// Before, so nothing inside the handler reads its own stale entry...
		await InvalidateCacheAsync(message, cancellationToken);

		try
		{
			var result = await next(message, cancellationToken);

			// ...and after, so a concurrent read that repopulated the key mid-write does not outlive it.
			// CancellationToken.None deliberately: the write has happened, so the stale entry has to go
			// whether or not the caller is still interested. Passing the request token here would let a
			// cancellation leave exactly the poisoned entry this pass exists to remove.
			await InvalidateCacheAsync(message, CancellationToken.None);

			return result;
		}
		catch
		{
			// A handler that threw may still have committed part of its write before it did, and a
			// cancelled caller may have left one behind entirely — either way the second pass is more
			// necessary here than on the success path, not less.
			try
			{
				await InvalidateCacheAsync(message, CancellationToken.None);
			}
			catch
			{
				// Never replace the exception that brought us here with one from the cleanup: the
				// handler's failure is the one the caller has to see.
			}

			throw;
		}
	}

	private async ValueTask InvalidateCacheAsync(TRequest message, CancellationToken cancellationToken)
	{
		foreach (var key in message.CacheKeys)
		{
			await cache.RemoveAsync(key, token: cancellationToken);

			// A write to an object reaches every cached result holding a snapshot of it, not only
			// its node: the contents list it sits in, an occupant's location answer, a lookup by
			// name. Those carry the object's tag (see EmbeddedObjects); expire them with the key.
			if (CacheKeys.TryParseObjectNumber(key, out var number))
			{
				await cache.RemoveByTagAsync(CacheKeys.ObjectTag(number), token: cancellationToken);
			}
		}

		if (message.CacheTags.Length != 0)
		{
			await cache.RemoveByTagAsync(message.CacheTags, token: cancellationToken);
		}
	}
}