using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Builds the type-ancestor ^-listen contribution: the ancestor's own listen attributes plus those
/// along the ancestor's OWN LISTEN_PARENT chain (no ancestor-of-ancestor). Computed once per ancestor
/// and cached (see <see cref="GetAncestorListenAttributesQuery"/>) so the listen matcher does not
/// re-walk the ancestor chain for every listener that falls through to it. Invalidated when the
/// ancestor's attributes change via the shared <c>ancestor-listens:{ancestor}</c> key.
/// </summary>
public class GetAncestorListenAttributesQueryHandler(IMediator mediator)
	: IQueryHandler<GetAncestorListenAttributesQuery, ListenAttributeCache[]>
{
	private const int MaxParentDepth = 10;

	public async ValueTask<ListenAttributeCache[]> Handle(
		GetAncestorListenAttributesQuery request, CancellationToken cancellationToken)
	{
		var ancestorNode = await mediator.Send(new GetObjectNodeQuery(request.Ancestor), cancellationToken);
		if (ancestorNode.IsNone)
		{
			return [];
		}

		var ancestor = ancestorNode.Known;
		var result = new List<ListenAttributeCache>();
		var visited = new HashSet<int> { ancestor.Object().DBRef.Number };

		// The ancestor's own listen attributes (per-object listen set is itself cached).
		result.AddRange(await mediator.Send(new GetListenAttributesQuery(ancestor), cancellationToken));

		// Honor the ancestor's own LISTEN_PARENT chain, then stop.
		var current = ancestor;
		var depth = 0;
		while (depth < MaxParentDepth)
		{
			var parentAsync = await current.Object().Parent.WithCancellation(cancellationToken);
			if (parentAsync.IsNone)
				break;

			var parent = parentAsync.Known;
			var parentObject = parent.Object();
			if (!visited.Add(parentObject.DBRef.Number))
				break;

			var hasListenParent = await parentObject.Flags.Value.AnyAsync(f => f.Name == "LISTEN_PARENT");
			if (!hasListenParent)
				break;

			result.AddRange(await mediator.Send(new GetListenAttributesQuery(parent), cancellationToken));

			current = parent;
			depth++;
		}

		return [.. result];
	}
}
