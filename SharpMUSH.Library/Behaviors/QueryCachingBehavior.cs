using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Queries.Database;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

public class QueryCachingBehavior<TRequest, TResponse>(IFusionCache cache)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IQuery<TResponse>, ICacheable
{
	public async ValueTask<TResponse> Handle(
		TRequest message,
		MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken
	)
	{
		// The factory runs under FusionCache's token, which carries the profile's hard timeout, so
		// a hung database call is cut loose from the command rather than holding its key lock.
		return await cache.GetOrSetAsync<TResponse>(message.CacheKey,
			async (ctx, ct) =>
			{
				var result = await next(message, ct);
				EmbeddedObjectTags.Apply(ctx, message, result);
				return result;
			},
			options: CacheEntryProfiles.For(message.Profile),
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);
	}
}

/// <summary>
/// Stamps a cached result with one <see cref="Definitions.CacheKeys.ObjectTag"/> per object it
/// embeds, so a write to any of them expires it (see <see cref="EmbeddedObjects"/>). The object
/// node's own query is left alone: its key is what a write removes, and a tag on it would cost it
/// the fail-safe that key-only entries are allowed. Any entry that does gain tags loses fail-safe
/// and eager refresh, the same rule the Tagged profile encodes statically.
/// </summary>
public static class EmbeddedObjectTags
{
	public static void Apply<TValue>(FusionCacheFactoryExecutionContext<TValue> ctx, ICacheable message, object? result)
	{
		if (message is GetObjectNodeByNumberQuery)
		{
			return;
		}

		var embedded = EmbeddedObjects.TagsFor(result);
		if (embedded.Length == 0)
		{
			return;
		}

		ctx.Tags = message.CacheTags.Length == 0 ? embedded : [.. message.CacheTags, .. embedded];
		ctx.Options.IsFailSafeEnabled = false;
		ctx.Options.EagerRefreshThreshold = null;
	}
}
