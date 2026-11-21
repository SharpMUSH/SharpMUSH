using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPlayerNamesQueryHandler(IObjectStore database)
	: IStreamQueryHandler<GetPlayerNamesQuery, PlayerNameEntry>
{
	public async IAsyncEnumerable<PlayerNameEntry> Handle(GetPlayerNamesQuery request,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		await foreach (var player in database.GetAllPlayersAsync(cancellationToken).WithCancellation(cancellationToken))
		{
			yield return new PlayerNameEntry(player.Object.DBRef, player.Object.Name, player.Aliases ?? []);
		}
	}
}
