using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class PowerQueryHandler(ISharpDatabase database): IQueryHandler<GetPowersQuery, IAsyncEnumerable<SharpPower>>
{
	public async ValueTask<IAsyncEnumerable<SharpPower>> Handle(GetPowersQuery query, CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetObjectPowersAsync(cancellationToken);
	}
}