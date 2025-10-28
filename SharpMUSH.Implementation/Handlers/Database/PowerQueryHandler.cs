using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class PowerQueryHandler(ISharpDatabase database): IQueryHandler<GetPowersQuery, IEnumerable<SharpPower>>
{
	public async ValueTask<IEnumerable<SharpPower>> Handle(GetPowersQuery query, CancellationToken cancellationToken) 
		=> await database.GetObjectPowersAsync(cancellationToken);
}