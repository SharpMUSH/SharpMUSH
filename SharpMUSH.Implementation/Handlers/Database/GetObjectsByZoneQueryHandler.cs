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
		var zone = request.Zone switch
		{
			DBRef zoneRef => await objects.GetObjectNodeAsync(zoneRef, cancellationToken),
			AnySharpObject known => known.WithNoneOption()
		};

		if (zone.IsNone)
		{
			yield break;
		}

		await foreach (var obj in database.GetObjectsByZoneAsync(zone.Known, cancellationToken)
			.WithCancellation(cancellationToken))
		{
			yield return obj;
		}
	}
}
