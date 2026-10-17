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
public class StreamQueryCachingBehavior<TRequest, TResponse>(IFusionCache cache, ObjectVersions versions, IMediator mediator)
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
			// On a miss the handler has just resolved every object through the node cache; those are what
			// a re-resolve of the stored refs would hand out, so a list this call stored is yielded as read
			// rather than resolved a second time.
			CachedValue<CachedObjectRefs>? produced = null;
			TResponse[] read = [];
			var stored = await cache.GetOrSetAsync<CachedValue<CachedObjectRefs>>(message.CacheKey,
				async (ctx, ct) =>
				{
					var epoch = versions.Epoch;
					var values = await next(message, ct)
						.Where(value => ObjectShaped<TResponse>.RefOf(value).HasValue)
						.ToArrayAsync(ct);
					var refs = values.Select(value => ObjectShaped<TResponse>.RefOf(value)!.Value).ToArray();
					ctx.Options.Size = Math.Clamp(refs.Length, 1, CacheEntryProfiles.MaxEntrySize);
					EmbeddedObjectTags.Apply(ctx, message, refs.Select(r => r.Number));
					read = values;
					return produced = new CachedValue<CachedObjectRefs>(new CachedObjectRefs(refs), epoch, ctx.Tags ?? []);
				},
				options: CacheEntryProfiles.For(message.Profile),
				tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
				token: cancellationToken);

			if (!stored.IsCurrent(versions))
			{
				await cache.RemoveAsync(message.CacheKey, token: CancellationToken.None);
				if (!ReferenceEquals(stored, produced))
				{
					await foreach (var value in next(message, cancellationToken).WithCancellation(cancellationToken)) yield return value;
					yield break;
				}
			}

			if (ReferenceEquals(stored, produced))
			{
				foreach (var value in read)
				{
					// The resolved values are already in hand; the token still ends the walk, as each lookup did.
					cancellationToken.ThrowIfCancellationRequested();
					yield return value;
				}

				yield break;
			}

			foreach (var reference in stored.Value.Refs)
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

		CachedValue<List<TResponse>>? producedList = null;
		var list = await cache.GetOrSetAsync<CachedValue<List<TResponse>>>(message.CacheKey,
			async (ctx, ct) =>
			{
				var epoch = versions.Epoch;
				var result = await next(message, ct).ToListAsync(ct);
				// A list weighs what it holds against the memory cache's size limit: a room with three
				// hundred things in it is not the same cost as an empty one. Capped so that a list larger
				// than the cache is still cacheable rather than refused and recomputed on every read.
				ctx.Options.Size = Math.Clamp(result.Count, 1, CacheEntryProfiles.MaxEntrySize);
				EmbeddedObjectTags.Apply(ctx, message, result);
				return producedList = new CachedValue<List<TResponse>>(result, epoch, ctx.Tags ?? []);
			},
			options: CacheEntryProfiles.For(message.Profile),
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);

		if (!list.IsCurrent(versions))
		{
			await cache.RemoveAsync(message.CacheKey, token: CancellationToken.None);
			if (!ReferenceEquals(list, producedList))
			{
				await foreach (var value in next(message, cancellationToken).WithCancellation(cancellationToken)) yield return value;
				yield break;
			}
		}

		foreach (var item in list.Value)
		{
			yield return item;
		}
	}
}
