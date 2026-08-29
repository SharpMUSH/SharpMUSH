using Mediator;
using SharpMUSH.Library.Attributes;
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
/// Both passes go through <c>RemoveByTagAsync</c>, including for the targeted <see cref="CacheKeys"/>:
/// every entry is tagged with its own key (<see cref="CacheEntryTags"/>) precisely so that it can be.
/// <c>RemoveAsync</c> alone drops only what is in the cache at that instant, which leaves the window
/// this class used to document as open — a read that issued its query before the commit and stores its
/// answer after the second pass. A tag invalidation is a recorded event rather than a deletion, and
/// FusionCache resolves it against when the entry's factory <em>began</em>, so that late store is
/// recognised as pre-write and is never served. That is the "read side carries a version" this needed,
/// and issue #838 is what it cost while it was missing.
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
		// RemoveAsync as well as the tag pass: the marker is what makes the invalidation durable against
		// a straddling read, but it leaves the entry in memory until something replaces it, and a write
		// has no reason to keep paying for the old answer.
		foreach (var key in message.CacheKeys)
		{
			await cache.RemoveAsync(key, token: cancellationToken);
		}

		string[] tokens = [.. message.CacheKeys, .. message.CacheTags];
		if (tokens.Length != 0)
		{
			await cache.RemoveByTagAsync(tokens, token: cancellationToken);
		}
	}
}