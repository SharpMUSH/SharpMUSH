using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPlayerQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetPlayerQuery, IAsyncEnumerable<SharpPlayer>>
{
	public async ValueTask<IAsyncEnumerable<SharpPlayer>> Handle(GetPlayerQuery request, CancellationToken cancellationToken)
		=> await database.GetPlayerByNameOrAliasAsync(request.Name);
}