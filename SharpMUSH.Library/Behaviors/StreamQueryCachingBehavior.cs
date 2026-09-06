using Mediator;
using SharpMUSH.Library.Attributes;
using System.Runtime.CompilerServices;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Caches <see cref="IStreamQuery{TResponse}"/> results that implement <see cref="ICacheable"/>.
/// On cache miss the stream is materialized to a list, stored in FusionCache, and yielded.
/// On cache hit the stored list is yielded directly.
/// </summary>
public class StreamQueryCachingBehavior<TRequest, TResponse>(IFusionCache cache)
	: IStreamPipelineBehavior<TRequest, TResponse>
	where TRequest : IStreamQuery<TResponse>, ICacheable
{
	public async IAsyncEnumerable<TResponse> Handle(
		TRequest message,
		StreamHandlerDelegate<TRequest, TResponse> next,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		var list = await cache.GetOrSetAsync<List<TResponse>>(message.CacheKey,
			async (ctx, ct) =>
			{
				var result = await MaterializeAsync(message, next, ct);
				// A list weighs what it holds against the memory cache's size limit: a room with three
				// hundred things in it is not the same cost as an empty one.
				ctx.Options.Size = Math.Max(1, result.Count);
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
