using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetPowerQueryHandler(ISharpDatabase database) : IQueryHandler<GetPowerQuery, SharpPower?>
{
	public async ValueTask<SharpPower?> Handle(GetPowerQuery query, CancellationToken cancellationToken)
	{
		var exactMatch = await database.GetPowerAsync(query.PowerName, cancellationToken);
		if (exactMatch is not null)
		{
			return exactMatch;
		}

		// Fall back to case-insensitive match on name or alias
		return await database.GetObjectPowersAsync(cancellationToken)
			.FirstOrDefaultAsync(
				p => p.Name.Equals(query.PowerName, StringComparison.InvariantCultureIgnoreCase)
					|| (p.Alias != null && p.Alias.Equals(query.PowerName, StringComparison.InvariantCultureIgnoreCase)),
				cancellationToken);
	}
}
