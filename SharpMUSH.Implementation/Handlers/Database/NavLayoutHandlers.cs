using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetNavLayoutQueryHandler(IPortalNavigationStore store) : IQueryHandler<GetNavLayoutQuery, Found<NavLayout>>
{
	public ValueTask<Found<NavLayout>> Handle(GetNavLayoutQuery query, CancellationToken cancellationToken)
		=> store.GetNavLayoutAsync(cancellationToken);
}

public class SaveNavLayoutCommandHandler(IPortalNavigationStore store) : ICommandHandler<SaveNavLayoutCommand, NavLayoutSaveResult>
{
	public ValueTask<NavLayoutSaveResult> Handle(SaveNavLayoutCommand command, CancellationToken cancellationToken)
		=> store.SaveNavLayoutAsync(command.Layout, command.ExpectedVersion, cancellationToken);
}

public class ResetNavLayoutCommandHandler(IPortalNavigationStore store) : ICommandHandler<ResetNavLayoutCommand>
{
	public async ValueTask<Unit> Handle(ResetNavLayoutCommand command, CancellationToken cancellationToken)
	{
		await store.ResetNavLayoutAsync(cancellationToken);
		return Unit.Value;
	}
}
