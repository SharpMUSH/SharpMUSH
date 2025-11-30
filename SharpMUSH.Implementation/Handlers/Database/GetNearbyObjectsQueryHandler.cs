using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetNearbyObjectsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetNearbyObjectsQuery, AnySharpObject>
{
	public async IAsyncEnumerable<AnySharpObject> Handle(GetNearbyObjectsQuery request, CancellationToken cancellationToken)
	{
		switch (request.DBRef)
		{
			case { IsT0: true, AsT0: var dbrefQuery }:
			{
				await foreach (var item in (await database.GetNearbyObjectsAsync(dbrefQuery, cancellationToken))
				               .WithCancellation(cancellationToken))
				{
					yield return item;
				}

				break;
			}
			case { IsT1: true, AsT1: var objQuery }:
			{
				await foreach (var item in (await database.GetNearbyObjectsAsync(objQuery, cancellationToken))
				               .WithCancellation(cancellationToken))
				{
					yield return item;
				}

				break;
			}
			default:
			{
				throw new ArgumentException(nameof(request.DBRef));
			}
		}
	}
}
