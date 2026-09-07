using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using System.Runtime.CompilerServices;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Caches <see cref="IStreamQuery{TResponse}"/> results that implement <see cref="ICacheable"/>.
/// On cache miss the stream is materialized, stored in FusionCache, and yielded; on cache hit the
/// stored list is yielded directly.
/// </summary>
public class StreamQueryCachingBehavior<TRequest, TResponse>(IFusionCache cache, IMediator mediator)
	: IStreamPipelineBehavior<TRequest, TResponse>
	where TRequest : IStreamQuery<TResponse>, ICacheable
{
	// An object-shaped stream is stored as the dbrefs it names and resolved through the object node
	// cache on each read, so every result hands out the one instance per object (see IObjectShaped).
	private static readonly bool Shaped = ObjectShaped<TResponse>.Supported;

	public async IAsyncEnumerable<TResponse> Handle(
		TRequest message,
		StreamHandlerDelegate<TRequest, TResponse> next,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		if (Shaped)
		{
			var stored = await cache.GetOrSetAsync<CachedObjectRefs>(message.CacheKey,
				async (ctx, ct) =>
				{
					var result = await MaterializeAsync(message, next, ct);
					var refs = result.Select(ObjectShaped<TResponse>.RefOf).OfType<DBRef>().ToArray();
					ctx.Options.Size = Math.Clamp(refs.Length, 1, CacheEntryProfiles.MaxEntrySize);
					EmbeddedObjectTags.Apply(ctx, message, refs.Select(r => r.Number).ToArray());
					return new CachedObjectRefs(refs);
				},
				options: CacheEntryProfiles.For(message.Profile),
				tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
				token: cancellationToken);

			foreach (var reference in stored.Refs)
			{
				// An object gone since the list was stored is simply not in it any more, and the full
				// object id means a recycled number is gone too, not the object that took its place.
				var node = await mediator.Send(new GetObjectNodeQuery(reference), cancellationToken);
				if (ObjectShaped<TResponse>.TryFromNode(node, out var value))
				{
					yield return value;
				}
			}

			yield break;
		}

		var list = await cache.GetOrSetAsync<List<TResponse>>(message.CacheKey,
			async (ctx, ct) =>
			{
				var result = await MaterializeAsync(message, next, ct);
				// A list weighs what it holds against the memory cache's size limit: a room with three
				// hundred things in it is not the same cost as an empty one. Capped so that a list larger
				// than the cache is still cacheable rather than refused and recomputed on every read.
				ctx.Options.Size = Math.Clamp(result.Count, 1, CacheEntryProfiles.MaxEntrySize);
				EmbeddedObjectTags.Apply(ctx, message, result);
				return result;
			},
			options: CacheEntryProfiles.For(message.Profile),
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);

		foreach (var item in list)
		{
			yield return item;
		}
	}

	private static async ValueTask<List<TResponse>> MaterializeAsync(
		TRequest message,
		StreamHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		var result = new List<TResponse>();
		await foreach (var item in next(message, cancellationToken).WithCancellation(cancellationToken))
		{
			result.Add(item);
		}
		return result;
	}
}
