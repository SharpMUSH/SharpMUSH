using Mediator;
using OneOf;
using SharpMUSH.Database;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using System.Collections;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Implementation.Behaviors;

/// <summary>
/// Keeps the flags and powers of an object that lives inside another cached result coherent with
/// the object itself. A provider loads an object's relations with it, so an object node cached
/// under <c>object:#N</c> carries its own flags and powers, and every flag, power and lock write
/// removes that key. But the same object also appears inside other cached results - a room's
/// contents list, a location answer, a player-by-name lookup - and those entries are not removed
/// by a flag write. Left as loaded, a cached contents list would still show an object as it was
/// before <c>@set obj=DARK</c>. So, once per value as it is cached (these behaviours sit inside
/// the caching behaviours in the pipeline), every embedded object's flag and power reads are
/// re-pointed at the cached object node, which is the one entry a write does invalidate.
/// </summary>
/// <remarks>
/// The object node's own query (<see cref="GetObjectNodeByNumberQuery"/>) is the source and is
/// left alone. Locks on embedded objects are still the snapshot loaded with the list; that window
/// predates this and is tracked with the relation-loading follow-up.
/// </remarks>
public class RelationsThroughCacheBehavior<TRequest, TResponse>(IMediator mediator)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IQuery<TResponse>, ICacheable
{
	public async ValueTask<TResponse> Handle(TRequest message, MessageHandlerDelegate<TRequest, TResponse> next,
		CancellationToken cancellationToken)
	{
		var response = await next(message, cancellationToken);
		if (message is not GetObjectNodeByNumberQuery)
		{
			ObjectRelationsThroughCache.Apply(response, mediator);
		}

		return response;
	}
}

public class StreamRelationsThroughCacheBehavior<TRequest, TResponse>(IMediator mediator)
	: IStreamPipelineBehavior<TRequest, TResponse>
	where TRequest : IStreamQuery<TResponse>, ICacheable
{
	public async IAsyncEnumerable<TResponse> Handle(TRequest message, StreamHandlerDelegate<TRequest, TResponse> next,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		await foreach (var item in next(message, cancellationToken).WithCancellation(cancellationToken))
		{
			ObjectRelationsThroughCache.Apply(item, mediator);
			yield return item;
		}
	}
}

/// <summary>The re-pointing itself, for any value shape a query returns.</summary>
public static class ObjectRelationsThroughCache
{
	public static void Apply(object? value, IMediator mediator)
	{
		switch (value)
		{
			case null:
			case string:
				return;
			case SharpObject obj:
				RePoint(obj, mediator);
				return;
			case SharpPlayer p:
				RePoint(p.Object, mediator);
				return;
			case SharpRoom r:
				RePoint(r.Object, mediator);
				return;
			case SharpExit e:
				RePoint(e.Object, mediator);
				return;
			case SharpThing t:
				RePoint(t.Object, mediator);
				return;
			case IOneOf union:
				Apply(union.Value, mediator);
				return;
			case IEnumerable items:
				foreach (var item in items)
				{
					Apply(item, mediator);
				}

				return;
		}
	}

	private static void RePoint(SharpObject obj, IMediator mediator)
	{
		var dbref = obj.DBRef;
		obj.Flags = new(() => new FreshAsyncEnumerable<SharpObjectFlag>(ct => ViaNode(mediator, dbref, node => node.Flags, ct)));
		obj.Powers = new(() => new FreshAsyncEnumerable<SharpPower>(ct => ViaNode(mediator, dbref, node => node.Powers, ct)));
	}

	private static async IAsyncEnumerable<T> ViaNode<T>(IMediator mediator, DBRef dbref,
		Func<SharpObject, Lazy<IAsyncEnumerable<T>>> relation, [EnumeratorCancellation] CancellationToken ct)
	{
		var node = await mediator.Send(new GetObjectNodeQuery(dbref), ct);
		if (node.IsNone)
		{
			// Destroyed (or its number recycled) since the containing result was cached: it has no
			// flags to report, and the containing entry's own invalidation is what removes it.
			yield break;
		}

		await foreach (var item in relation(node.Known().Object()).Value.WithCancellation(ct))
		{
			yield return item;
		}
	}
}
