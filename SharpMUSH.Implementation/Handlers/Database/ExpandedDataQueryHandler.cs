using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ExpandedDataQueryHandler(ISharpDatabase database): IQueryHandler<ExpandedDataQuery, string?>
{
	public async ValueTask<string?> Handle(ExpandedDataQuery query, CancellationToken cancellationToken)
	{
		return await database.GetExpandedObjectData(query.SharpObject.Id!, query.TypeName);
	}
}