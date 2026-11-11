using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetZonesOfQueryHandler(IZoneListStore store, IMediator mediator)
	: IQueryHandler<GetZonesOfQuery, IReadOnlyList<AnySharpObject>>
{
	public async ValueTask<IReadOnlyList<AnySharpObject>> Handle(GetZonesOfQuery query, CancellationToken cancellationToken)
	{
		var zones = new List<AnySharpObject>();
		foreach (var zone in await store.GetZoneRefsAsync(query.Object, cancellationToken))
		{
			if (await mediator.Send(new GetObjectNodeQuery(zone), cancellationToken) is AnySharpObject found)
			{
				zones.Add(found);
			}
		}

		return zones;
	}
}

public class SetObjectZonesCommandHandler(IZoneListStore store) : ICommandHandler<SetObjectZonesCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetObjectZonesCommand request, CancellationToken cancellationToken)
	{
		await store.SetZonesAsync(request.Target.Object().DBRef, [.. request.Zones.Select(zone => zone.Object().DBRef)],
			cancellationToken);
		return Unit.Value;
	}
}
