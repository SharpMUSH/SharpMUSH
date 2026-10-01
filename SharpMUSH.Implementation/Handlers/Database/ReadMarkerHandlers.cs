using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetReadMarkersQueryHandler(IReadMarkerStore store)
	: IQueryHandler<GetReadMarkersQuery, IReadOnlyList<SharpReadMarker>>
{
	public ValueTask<IReadOnlyList<SharpReadMarker>> Handle(GetReadMarkersQuery query, CancellationToken cancellationToken)
		=> store.GetReadMarkersAsync(query.Character, cancellationToken);
}

public class AdvanceReadMarkerCommandHandler(IReadMarkerStore store)
	: ICommandHandler<AdvanceReadMarkerCommand, SharpReadMarker>
{
	public ValueTask<SharpReadMarker> Handle(AdvanceReadMarkerCommand command, CancellationToken cancellationToken)
		=> store.AdvanceReadMarkerAsync(command.Character, command.Marker, cancellationToken);
}
