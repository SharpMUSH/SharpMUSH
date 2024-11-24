using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPlayerQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetPlayerQuery, IEnumerable<SharpPlayer>>
{
		public async ValueTask<IEnumerable<SharpPlayer>> Handle(GetPlayerQuery request, CancellationToken cancellationToken)
			=> await database.GetPlayerByNameAsync(request.Name);
}