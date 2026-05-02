using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectsByZoneQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetObjectsByZoneQuery, SharpObject>
{
	public async IAsyncEnumerable<SharpObject> Handle(GetObjectsByZoneQuery request, CancellationToken cancellationToken)
	{
		AnySharpObject zone;

		if (request.Zone.IsDBRef)
		{
			var maybeZone = await database.GetObjectNodeAsync(request.Zone.AsDBRef, cancellationToken);
			if (maybeZone.IsNone)
			{
				yield break;
			}
			zone = maybeZone.Known;
		}
		else
		{
			zone = request.Zone.AsObject;
		}

		await foreach (var obj in database.GetObjectsByZoneAsync(zone, cancellationToken)
			.WithCancellation(cancellationToken))
		{
			yield return obj;
		}
	}
}
