using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPlayerQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetPlayerQuery, SharpPlayer>
{
	public IAsyncEnumerable<SharpPlayer> Handle(GetPlayerQuery request, CancellationToken cancellationToken)
		=>  database.GetPlayerByNameOrAliasAsync(request.Name, cancellationToken).AsTask().GetAwaiter().GetResult();
}