using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAllObjectFlagsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAllObjectFlagsQuery, IAsyncEnumerable<SharpObjectFlag>>
{
	public async ValueTask<IAsyncEnumerable<SharpObjectFlag>> Handle(GetAllObjectFlagsQuery request,
		CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetObjectFlagsAsync(cancellationToken);
	}
}

public class GetObjectFlagQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetObjectFlagQuery, SharpObjectFlag?>
{
	public async ValueTask<SharpObjectFlag?> Handle(GetObjectFlagQuery request,
		CancellationToken cancellationToken)
	{
		return await database.GetObjectFlagAsync(request.FlagName, cancellationToken);
	}
}

public class GetObjectFlagsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetObjectFlagsQuery, IAsyncEnumerable<SharpObjectFlag>?>
{
	public async ValueTask<IAsyncEnumerable<SharpObjectFlag>?> Handle(GetObjectFlagsQuery request,
		CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetObjectFlagsAsync(request.Id, cancellationToken);
	}
}