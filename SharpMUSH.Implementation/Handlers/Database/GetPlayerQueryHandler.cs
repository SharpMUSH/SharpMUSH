using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPlayerQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetPlayerQuery, SharpPlayer>
{
	public async IAsyncEnumerable<SharpPlayer> Handle(GetPlayerQuery request, CancellationToken cancellationToken)
	{
		await foreach (var item in (await database.GetPlayerByNameOrAliasAsync(request.Name, cancellationToken)).WithCancellation(cancellationToken))
		{
			yield return item;
		}
	}
}