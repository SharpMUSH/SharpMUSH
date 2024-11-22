using MediatR;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(ISharpDatabase database)
	: IRequestHandler<GetAttributeQuery, IEnumerable<SharpAttribute>?>
{
	public async Task<IEnumerable<SharpAttribute>?> Handle(GetAttributeQuery request, CancellationToken cancellationToken)
	{
		return await database.GetAttributeAsync(request.DBRef, request.Attribute).AsTask();
	}
}