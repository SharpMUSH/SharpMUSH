using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ExpandedDataQueryHandler(ISharpDatabase database): IQueryHandler<ExpandedDataQuery, string?>, IQueryHandler<ExpandedServerDataQuery, string?>
{
	public async ValueTask<string?> Handle(ExpandedDataQuery query, CancellationToken cancellationToken) 
		=> await database.GetExpandedObjectData(query.SharpObject.Id!, query.TypeName, cancellationToken);

	public async ValueTask<string?> Handle(ExpandedServerDataQuery query, CancellationToken cancellationToken) 
		=> await database.GetExpandedServerData(query.TypeName, cancellationToken);
}