using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Builds the type-ancestor command-attribute contribution: the ancestor's own $commands plus those
/// inherited along the ancestor's OWN @parent chain (no ancestor-of-ancestor). Scanned in isolation
/// (fresh seen/no_command accumulators) so the result depends only on the ancestor subtree and can be
/// cached per ancestor (see <see cref="GetAncestorCommandAttributesQuery"/>) and merged into each child
/// object's command set cheaply. Cached automatically by QueryCachingBehavior; invalidated when the
/// ancestor's attributes change via the shared <c>commands:{ancestor}</c> invalidation key.
/// </summary>
public class GetAncestorCommandAttributesQueryHandler(IMediator mediator)
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

		// Honor the ancestor's own parent chain, then stop (no ancestor-of-ancestor).
		var ancestorCurrent = ancestorObj;
		while (true)
		{
			var ancestorParent = await ancestorCurrent.Parent.WithCancellation(cancellationToken);
			if (ancestorParent.IsNone) break;

			var ancestorParentObj = ancestorParent.Known.Object();
			await CommandAttributeScanner.ScanAttributes(ancestorParentObj.AllAttributes.Value, commandAttributes,
				seenNames, noCommandPrefixes, isLocal: false, cancellationToken);

			ancestorCurrent = ancestorParentObj;
		}

		return [.. commandAttributes];
	}
}
