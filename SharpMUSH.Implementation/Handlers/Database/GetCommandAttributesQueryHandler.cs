using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler that builds command attribute cache by scanning object attributes once
/// and pre-compiling all regex patterns. Results are cached automatically by QueryCachingBehavior.
/// Traverses the parent chain for inherited $commands (respecting no_inherit and tree-level
/// no_command). The type ancestor is not consulted: atr_comm_match walks parents with
/// <c>next_parent(thing, current, &amp;parent_count, NULL)</c> (<c>src/attrib.c:1923</c>), and a NULL
/// <c>use_ancestor</c> never reaches ANCESTOR_* (<c>src/utils.c:891-910</c>).
/// </summary>
public class GetCommandAttributesQueryHandler(
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: IQueryHandler<GetCommandAttributesQuery, CommandAttributeCache[]>
{
	public async ValueTask<CommandAttributeCache[]> Handle(GetCommandAttributesQuery request, CancellationToken cancellationToken)
	{
		var sharpObj = request.SharpObject;
		var commandAttributes = new List<CommandAttributeCache>();
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var noCommandPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		await CommandAttributeScanner.ScanAttributes(sharpObj.Object().AllAttributes.Value, commandAttributes, seenNames,
			noCommandPrefixes, isLocal: true, cancellationToken);

		// Walk parent chain for inherited commands, capped at Limit.MaxParents with a cycle guard -
		// mirrors AttributeService.ParentChainAsync. Defence in depth: the write-side guards
		// (SafeToAddParentAsync, ExceedsMaxParentDepthAsync) should already keep a cycle from existing.
		var maxDepth = (int)configuration.CurrentValue.Limit.MaxParents;
		var visited = new HashSet<int> { sharpObj.Object().DBRef.Number };
		var current = sharpObj.Object();
		for (var depth = 0; depth < maxDepth; depth++)
		{
			if (await current.Parent.WithCancellation(cancellationToken) is not AnySharpObject parent) break;

			var parentObj = parent.Object();
			if (!visited.Add(parentObj.DBRef.Number)) break;

			await CommandAttributeScanner.ScanAttributes(parentObj.AllAttributes.Value, commandAttributes, seenNames,
				noCommandPrefixes, isLocal: false, cancellationToken);

			current = parentObj;
		}

		return [.. commandAttributes];
	}
}
