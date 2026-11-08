using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

public class QueryCachingBehavior<TRequest, TResponse>(IFusionCache cache, ObjectVersions versions, IMediator mediator)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IQuery<TResponse>, ICacheable
{
	// The object node query is the object cache itself; every other object-shaped result is stored
	// as the dbref it names and resolved through that cache on each read (see IObjectShaped).
	private static readonly bool Shaped =
		typeof(TRequest) != typeof(GetObjectNodeByNumberQuery) && ObjectShaped<TResponse>.Supported;

	public ValueTask<TResponse> Handle(
		TRequest message,
		MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken
	)
	{
		if (Shaped)
		{
			return HandleShapedAsync(message, next, cancellationToken);
		}

		if (message is not GetObjectNodeByNumberQuery
			&& !(message.Profile == CacheEntryProfile.Object && CacheKeys.TryParseObjectNumber(message.CacheKey, out _)))
		{
			return HandleValueAsync(message, next, cancellationToken);
		}

		return HandleObjectAsync(message, next, cancellationToken);
	}

	private async ValueTask<TResponse> HandleObjectAsync(TRequest message, MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		// The certificate belongs to the factory, not the caller waiting for it. A reader arriving
		// after a completed write can still join a pre-write factory inside FusionCache's key lock.
		// Its own starting version is already current, so comparing only that version misses the
		// stale answer. Keep the object tag in the certificate only: adding it to FusionCache tags
		// would disable the object profile's fail-safe and eager refresh.
		var number = 0;
		var versioned = message.Profile == CacheEntryProfile.Object
										&& CacheKeys.TryParseObjectNumber(message.CacheKey, out number);
		CachedValue<TResponse>? produced = null;
		var stored = await cache.GetOrSetAsync<CachedValue<TResponse>>(message.CacheKey,
			async (ctx, ct) =>
			{
				var epoch = versions.Epoch;
				var result = await next(message, ct);
				EmbeddedObjectTags.Apply(ctx, message, result);
				return produced = new CachedValue<TResponse>(result, epoch,
					versioned ? [CacheKeys.ObjectTag(number)] : []);
			},
			options: CacheEntryProfiles.For(message.Profile),
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null,
			token: cancellationToken);

		if (!stored.IsCurrent(versions))
		{
			// Move the version on both sides so ObjectRelationMemo cannot retain an answer from
			// this stale fill. Only its original reader may keep the snapshot it actually read;
			// joining callers read the handler once directly, without another cache retry loop.
			versions.Bump(number);
			await cache.RemoveAsync(message.CacheKey, token: CancellationToken.None);
			versions.Bump(number);
			if (!ReferenceEquals(stored, produced)) return await next(message, cancellationToken);
		}

		return stored.Value;
	}

	private async ValueTask<TResponse> HandleValueAsync(TRequest message, MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		CachedValue<TResponse>? produced = null;
		var stored = await cache.GetOrSetAsync<CachedValue<TResponse>>(message.CacheKey,
			async (ctx, ct) =>
			{
				var epoch = versions.Epoch;
				var result = await next(message, ct);
				EmbeddedObjectTags.Apply(ctx, message, result);
				return produced = new CachedValue<TResponse>(result, epoch, ctx.Tags ?? []);
			}, options: CacheEntryProfiles.For(message.Profile),
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null, token: cancellationToken);
		if (!stored.IsCurrent(versions))
		{
			await cache.RemoveAsync(message.CacheKey, token: CancellationToken.None);
			if (!ReferenceEquals(stored, produced)) return await next(message, cancellationToken);
		}
		return stored.Value;
	}

	private async ValueTask<TResponse> HandleShapedAsync(TRequest message, MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		CachedValue<CachedObjectRef>? produced = null;
		TResponse read = default!;
		var stored = await cache.GetOrSetAsync<CachedValue<CachedObjectRef>>(message.CacheKey,
			async (ctx, ct) =>
			{
				var epoch = versions.Epoch;
				var result = await next(message, ct);
				var reference = ObjectShaped<TResponse>.RefOf(result);
				EmbeddedObjectTags.Apply(ctx, message, reference is { } named ? [named.Number] : []);
				read = result;
				return produced = new CachedValue<CachedObjectRef>(new CachedObjectRef(reference), epoch, ctx.Tags ?? []);
			}, options: CacheEntryProfiles.For(message.Profile),
			tags: message.CacheTags.Length > 0 ? message.CacheTags : null, token: cancellationToken);

		if (!stored.IsCurrent(versions))
		{
			await cache.RemoveAsync(message.CacheKey, token: CancellationToken.None);
			return ReferenceEquals(stored, produced) ? read : await next(message, cancellationToken);
		}

		var node = stored.Value.Ref is { } known
			? await mediator.Send(new GetObjectNodeQuery(known), cancellationToken)
			: new None();
		if (ObjectShaped<TResponse>.TryFromNode(node, out var value)) return value;
		await cache.RemoveAsync(message.CacheKey, token: CancellationToken.None);
		return await next(message, cancellationToken);
	}
}

/// <summary>
/// Stamps a cached result with one <see cref="Definitions.CacheKeys.ObjectTag"/> per object it
/// names, so a write to any of them expires it (see <see cref="EmbeddedObjects"/> and
/// <see cref="IObjectShaped{TSelf}"/>). The object node's own query is left alone: its key is what a write
/// removes, and a tag on it would cost it the fail-safe that key-only entries are allowed. Any
/// entry that does gain tags loses fail-safe and eager refresh, the same rule the Tagged profile
/// encodes statically.
/// </summary>
public static class EmbeddedObjectTags
{
	public static void Apply<TValue>(FusionCacheFactoryExecutionContext<TValue> ctx, ICacheable message, object? result)
		=> Apply(ctx, message, EmbeddedObjects.TagsFor(result));

	public static void Apply<TValue>(FusionCacheFactoryExecutionContext<TValue> ctx, ICacheable message, IEnumerable<int> numbers)
		=> Apply(ctx, message, numbers.Select(CacheKeys.ObjectTag).ToArray());

	private static void Apply<TValue>(FusionCacheFactoryExecutionContext<TValue> ctx, ICacheable message, string[] embedded)
	{
		// The node query's entry is what a write removes; a tag on it would cost it its fail-safe.
		if (embedded.Length == 0 || message is GetObjectNodeByNumberQuery || !message.TagsEmbeddedObjects)
		{
			return;
		}

		ctx.Tags = message.CacheTags.Length == 0 ? embedded : [.. message.CacheTags, .. embedded];
		ctx.Options.IsFailSafeEnabled = false;
		ctx.Options.EagerRefreshThreshold = null;
	}
}
