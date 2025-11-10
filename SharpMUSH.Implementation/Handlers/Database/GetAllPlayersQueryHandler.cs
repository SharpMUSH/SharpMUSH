using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAllPlayersQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAllPlayersQuery, SharpPlayer>
{
	public IAsyncEnumerable<SharpPlayer> Handle(GetAllPlayersQuery request, CancellationToken cancellationToken)
		=> database.GetAllPlayersAsync(cancellationToken);
}
