using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class RecordRollCommandHandler(IRollStore store, TimeProvider clock) : ICommandHandler<RecordRollCommand, RecordedRoll>
{
	public ValueTask<RecordedRoll> Handle(RecordRollCommand command, CancellationToken cancellationToken)
		=> store.RecordRollAsync(command.Draft with { At = clock.GetUtcNow() }, cancellationToken);
}

public class GetRollQueryHandler(IRollStore store) : IQueryHandler<GetRollQuery, Found<RecordedRoll>>
{
	public ValueTask<Found<RecordedRoll>> Handle(GetRollQuery query, CancellationToken cancellationToken)
		=> store.GetRollAsync(query.Id, cancellationToken);
}
