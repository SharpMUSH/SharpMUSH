using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeEntryQueryHandler(ISharpDatabase database) : IQueryHandler<GetAttributeEntryQuery, SharpAttributeEntry?>
{
	public async ValueTask<SharpAttributeEntry?> Handle(GetAttributeEntryQuery query, CancellationToken cancellationToken)
	{
		return await database.GetSharpAttributeEntry(query.Name, cancellationToken);
	}
}
