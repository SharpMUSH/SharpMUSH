using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class MergeActivityCommandHandler(IActivityStore store) : ICommandHandler<MergeActivityCommand>
{
	public async ValueTask<Unit> Handle(MergeActivityCommand command, CancellationToken cancellationToken)
	{
		await store.MergeActivityAsync(command.Batch, cancellationToken);
		return Unit.Value;
	}
}

public class GetActivityQueryHandler(IActivityStore store) : IQueryHandler<GetActivityQuery, ActivityWindow>
{
	public ValueTask<ActivityWindow> Handle(GetActivityQuery query, CancellationToken cancellationToken)
		=> store.GetActivityAsync(query.FromDay, query.ToDay, cancellationToken);
}
