using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class RecordPageCommandHandler(IPageLogStore store) : ICommandHandler<RecordPageCommand>
{
	public async ValueTask<Unit> Handle(RecordPageCommand command, CancellationToken cancellationToken)
	{
		await store.RecordPageAsync(command.Page, command.Owners, cancellationToken);
		return Unit.Value;
	}
}

public class PurgePageLogCommandHandler(IPageLogStore store) : ICommandHandler<PurgePageLogCommand, int>
{
	public ValueTask<int> Handle(PurgePageLogCommand command, CancellationToken cancellationToken)
		=> store.PurgePageLogAsync(command.Before, cancellationToken);
}

public class GetPageLogQueryHandler(IPageLogStore store) : IQueryHandler<GetPageLogQuery, IReadOnlyList<SharpPage>>
{
	public ValueTask<IReadOnlyList<SharpPage>> Handle(GetPageLogQuery query, CancellationToken cancellationToken)
		=> store.GetPageLogAsync(query.Character, query.With, query.Lines, cancellationToken);
}

public class GetPageConversationsQueryHandler(IPageLogStore store)
	: IQueryHandler<GetPageConversationsQuery, IReadOnlyList<SharpPageConversation>>
{
	public ValueTask<IReadOnlyList<SharpPageConversation>> Handle(GetPageConversationsQuery query,
		CancellationToken cancellationToken)
		=> store.GetPageConversationsAsync(query.Character, query.Limit, cancellationToken);
}

public class GetRecentPagesQueryHandler(IPageLogStore store) : IQueryHandler<GetRecentPagesQuery, IReadOnlyList<SharpPage>>
{
	public ValueTask<IReadOnlyList<SharpPage>> Handle(GetRecentPagesQuery query, CancellationToken cancellationToken)
		=> store.GetRecentPagesAsync(query.Character, query.Lines, cancellationToken);
}
