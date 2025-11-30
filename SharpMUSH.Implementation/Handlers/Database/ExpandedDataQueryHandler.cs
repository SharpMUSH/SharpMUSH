using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ExpandedDataQueryHandler(ISharpDatabase database): IQueryHandler<ExpandedDataQuery, object?>, IQueryHandler<ExpandedServerDataQuery, object?>
{
	public async ValueTask<object?> Handle(ExpandedDataQuery query, CancellationToken cancellationToken) 
		=> await database.GetExpandedObjectData<object?>(query.SharpObject.Id!, query.TypeName, cancellationToken);

	public async ValueTask<object?> Handle(ExpandedServerDataQuery query, CancellationToken cancellationToken) 
		=> await database.GetExpandedServerData(query.TypeName, cancellationToken);
}