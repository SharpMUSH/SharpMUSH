using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class PowerQueryHandler(ISharpDatabase database): IStreamQueryHandler<GetPowersQuery, SharpPower>
{
	public IAsyncEnumerable<SharpPower> Handle(GetPowersQuery query, CancellationToken cancellationToken) 
		=> database.GetObjectPowersAsync(cancellationToken);
}