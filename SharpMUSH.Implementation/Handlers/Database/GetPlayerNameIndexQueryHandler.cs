using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPlayerNameIndexQueryHandler(IObjectStore database)
	: IQueryHandler<GetPlayerNameIndexQuery, PlayerNameIndex>
{
	public async ValueTask<PlayerNameIndex> Handle(GetPlayerNameIndexQuery request, CancellationToken cancellationToken)
	{
		var players = await database.GetAllPlayersAsync(cancellationToken)
			.Select(player => new PlayerNameEntry(player.Object.DBRef, player.Object.Name, player.Aliases ?? []))
			.ToListAsync(cancellationToken);
		return PlayerNameIndex.Build(players, request.Aliases, request.FirstNames);
	}
}
