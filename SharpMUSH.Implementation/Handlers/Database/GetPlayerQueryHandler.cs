using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Streams the refs of the players a name or alias finds and resolves each through the object node cache,
/// so a miss builds each player once (see <see cref="GetContentsQueryHandler"/>).
/// </summary>
public class GetPlayerQueryHandler(IObjectStore database, IMediator mediator)
	: IStreamQueryHandler<GetPlayerQuery, SharpPlayer>
{
	public IAsyncEnumerable<SharpPlayer> Handle(GetPlayerQuery request, CancellationToken cancellationToken)
		=> ObjectRefs.ResolveAsync<SharpPlayer>(mediator, database.GetPlayerRefsByNameOrAliasAsync(request.Name, cancellationToken),
			cancellationToken);
}
