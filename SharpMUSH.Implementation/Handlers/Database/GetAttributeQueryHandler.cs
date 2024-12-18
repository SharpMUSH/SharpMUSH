using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributeQuery, IEnumerable<SharpAttribute>?>
{
	public async ValueTask<IEnumerable<SharpAttribute>?> Handle(GetAttributeQuery request, CancellationToken cancellationToken)
		=> await database.GetAttributeAsync(request.DBRef, request.Attribute);
}

public class GetAttributesQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributesQuery, IEnumerable<SharpAttribute>?>
{
	public async ValueTask<IEnumerable<SharpAttribute>?> Handle(GetAttributesQuery request, CancellationToken cancellationToken)
		=> await database.GetAttributesAsync(request.DBRef, request.Pattern);
}