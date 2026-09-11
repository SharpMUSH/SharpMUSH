using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectsByZoneQueryHandler(INavigationStore database, IObjectStore objects)
	: IStreamQueryHandler<GetObjectsByZoneQuery, SharpObject>
{
	public async IAsyncEnumerable<SharpObject> Handle(GetObjectsByZoneQuery request, CancellationToken cancellationToken)
	{
		AnySharpObject zone;

		if (request.Zone is DBRef zoneRef)
		{
			var maybeZone = await objects.GetObjectNodeAsync(zoneRef, cancellationToken);
			if (maybeZone.IsNone)
			{
				yield break;
			}
			zone = maybeZone.Known;
		}
		else
		{
			zone = (AnySharpObject)request.Zone.Value!;
		}

		await foreach (var obj in database.GetObjectsByZoneAsync(zone, cancellationToken)
			.WithCancellation(cancellationToken))
		{
			yield return obj;
		}
	}
}
