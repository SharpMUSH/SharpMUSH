using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler that builds command attribute cache by scanning object attributes once
/// and pre-compiling all regex patterns. Results are cached automatically by QueryCachingBehavior.
/// Traverses the parent chain, then the type ancestor (PennMUSH ANCESTOR_*), for inherited
/// $commands (respecting no_inherit and tree-level no_command).
/// </summary>
public class GetCommandAttributesQueryHandler(
	IMediator mediator,
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

		// Walk parent chain for inherited commands
		var current = sharpObj.Object();
		while (true)
		{
			var parent = await current.Parent.WithCancellation(cancellationToken);
			if (parent.IsNone) break;

			var parentObj = parent.Known.Object();
			await CommandAttributeScanner.ScanAttributes(parentObj.AllAttributes.Value, commandAttributes, seenNames,
				noCommandPrefixes, isLocal: false, cancellationToken);

			current = parentObj;
		}

		// PennMUSH ancestor fall-through: after the object's own @parent chain, merge in the type
		// ancestor's contribution (its own $commands + its own @parent chain, no ancestor-of-ancestor).
		//
		// Short-circuit cheapest-first: Ancestor() resolves the configured ancestor from type + config
		// with no DB access and returns null when disabled, so a disabled ancestor costs nothing here.
		// The ancestor's derived command set is itself cached (keyed by ancestor dbref) via
		// GetAncestorCommandAttributesQuery, so it is computed once per ancestor instead of being
		// rescanned for every child object that falls through to it.
		var ancestorRef = await sharpObj.Ancestor(configuration);
		if (ancestorRef is not null && ancestorRef.Value.Number != sharpObj.Object().DBRef.Number)
		{
			var ancestorCommands =
				await mediator.Send(new GetAncestorCommandAttributesQuery(ancestorRef.Value), cancellationToken);

			MergeAncestorCommands(ancestorCommands, commandAttributes, seenNames, noCommandPrefixes);
		}

		return [.. commandAttributes];
	}

	/// <summary>
	/// Merge the (cached, isolated) ancestor command contribution into the accumulator, applying the
	/// same gating the original single-pass scan applied when the ancestor was scanned last:
	/// child/parent attributes of the same name shadow the ancestor (<paramref name="seenNames"/>),
	/// and any <c>no_command</c> tree prefix accumulated from the object/parents blocks ancestor
	/// descendants (<paramref name="noCommandPrefixes"/>).
	/// </summary>
	private static void MergeAncestorCommands(
		CommandAttributeCache[] ancestorCommands,
		List<CommandAttributeCache> commandAttributes,
		HashSet<string> seenNames,
		HashSet<string> noCommandPrefixes)
	{
		foreach (var cached in ancestorCommands)
		{
			var longName = cached.Attribute.LongName;

			// Child/parent attribute of the same name already won — ancestor is shadowed.
			if (!seenNames.Add(longName))
				continue;

			// Blocked by a no_command tree prefix contributed by the object or its parents.
			if (noCommandPrefixes.Any(prefix => longName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
				continue;

			commandAttributes.Add(cached);
		}
	}
}
