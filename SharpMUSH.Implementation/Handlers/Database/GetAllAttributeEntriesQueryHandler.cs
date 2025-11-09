using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAllAttributeEntriesQueryHandler(ISharpDatabase database) : IStreamQueryHandler<GetAllAttributeEntriesQuery, SharpAttributeEntry>
{
	public IAsyncEnumerable<SharpAttributeEntry> Handle(GetAllAttributeEntriesQuery query, CancellationToken cancellationToken)
	{
		return database.GetAllAttributeEntriesAsync(cancellationToken);
	}
}
