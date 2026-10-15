using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeWatchCommandHandler(IAttributeStore store)
	: ICommandHandler<SetAttributeWatchCommand, Found<SharpAttributeEntry>>
{
	public ValueTask<Found<SharpAttributeEntry>> Handle(SetAttributeWatchCommand command, CancellationToken cancellationToken)
		=> store.SetAttributeWatchAsync(command.Name, command.Pattern, cancellationToken);
}

/// <summary>The watched entries, from the table's own listing: the table is small, and the answer is cached.</summary>
public class GetWatchedAttributesQueryHandler(IAttributeStore store) : IQueryHandler<GetWatchedAttributesQuery, WatchedAttributes>
{
	public async ValueTask<WatchedAttributes> Handle(GetWatchedAttributesQuery query, CancellationToken cancellationToken)
	{
		var patterns = new Dictionary<string, string>(StringComparer.Ordinal);
		await foreach (var entry in store.GetAllAttributeEntriesAsync(cancellationToken))
		{
			if (entry.Watch is { Length: > 0 } pattern)
			{
				patterns[entry.Name.ToUpperInvariant()] = pattern;
			}
		}

		return patterns.Count == 0 ? WatchedAttributes.None : new WatchedAttributes(patterns);
	}
}
