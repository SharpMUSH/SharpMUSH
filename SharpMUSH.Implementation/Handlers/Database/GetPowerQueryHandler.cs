using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPowerQueryHandler(ISharpDatabase database) : IQueryHandler<GetPowerQuery, SharpPower?>
{
	public async ValueTask<SharpPower?> Handle(GetPowerQuery query, CancellationToken cancellationToken)
	{
		return await database.GetPowerAsync(query.PowerName, cancellationToken);
	}
}
