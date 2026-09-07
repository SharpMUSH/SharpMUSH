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

		if (request.Zone.IsT0)
		{
			var maybeZone = await objects.GetObjectNodeAsync(request.Zone.AsT0, cancellationToken);
			if (maybeZone.IsNone)
			{
				yield break;
			}
			zone = maybeZone.Known;
		}
		else
		{
			zone = request.Zone.AsT1;
		}

		await foreach (var obj in database.GetObjectsByZoneAsync(zone, cancellationToken)
			.WithCancellation(cancellationToken))
		{
			yield return obj;
		}
	}
}
