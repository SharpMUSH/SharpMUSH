using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Builds the type-ancestor command-attribute contribution: the ancestor's own $commands plus those
/// inherited along the ancestor's OWN @parent chain (no ancestor-of-ancestor). Scanned in isolation
/// (fresh seen/no_command accumulators) so the result depends only on the ancestor subtree and can be
/// cached per ancestor (see <see cref="GetAncestorCommandAttributesQuery"/>) and merged into each child
/// object's command set cheaply. Cached automatically by QueryCachingBehavior; invalidated when the
/// ancestor's attributes change via the shared <c>commands:{ancestor}</c> invalidation key.
/// </summary>
public class GetAncestorCommandAttributesQueryHandler(
	IMediator mediator,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: IQueryHandler<GetAncestorCommandAttributesQuery, CommandAttributeCache[]>
{
	public async ValueTask<CommandAttributeCache[]> Handle(
		GetAncestorCommandAttributesQuery request, CancellationToken cancellationToken)
	{
		var ancestorNode = await mediator.Send(new GetObjectNodeQuery(request.Ancestor), cancellationToken);
		if (ancestorNode.IsNone)
		{
			return [];
		}

		var commandAttributes = new List<CommandAttributeCache>();
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var noCommandPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var ancestorObj = ancestorNode.Known.Object();

		// The ancestor's attributes are inherited BY the child, so they are scanned as non-local
		// (isLocal: false) — exactly as the original single-pass command scan did. A no_inherit
		// attribute on the ancestor (or a descendant of one) is therefore excluded, matching
		// PennMUSH ancestor inheritance semantics.
		await CommandAttributeScanner.ScanAttributes(ancestorObj.AllAttributes.Value, commandAttributes, seenNames,
			noCommandPrefixes, isLocal: false, cancellationToken);

		// Honor the ancestor's own parent chain, then stop (no ancestor-of-ancestor). Capped at
		// Limit.MaxParents with a cycle guard - mirrors AttributeService.ParentChainAsync. Defence
		// in depth: the write-side guards should already keep a cycle from existing.
		var maxDepth = (int)configuration.CurrentValue.Limit.MaxParents;
		var visited = new HashSet<int> { ancestorObj.DBRef.Number };
		var ancestorCurrent = ancestorObj;
		for (var depth = 0; depth < maxDepth; depth++)
		{
			var ancestorParent = await ancestorCurrent.Parent.WithCancellation(cancellationToken);
			if (ancestorParent.IsNone) break;

			var ancestorParentObj = ancestorParent.Known.Object();
			if (!visited.Add(ancestorParentObj.DBRef.Number)) break;

			await CommandAttributeScanner.ScanAttributes(ancestorParentObj.AllAttributes.Value, commandAttributes,
				seenNames, noCommandPrefixes, isLocal: false, cancellationToken);

			ancestorCurrent = ancestorParentObj;
		}

		return [.. commandAttributes];
	}
}
