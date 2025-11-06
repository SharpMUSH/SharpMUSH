using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAllObjectFlagsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAllObjectFlagsQuery, SharpObjectFlag>
{
	public IAsyncEnumerable<SharpObjectFlag> Handle(GetAllObjectFlagsQuery request,
		CancellationToken cancellationToken) =>
		database.GetObjectFlagsAsync(cancellationToken);
}

public class GetObjectFlagQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetObjectFlagQuery, SharpObjectFlag?>
{
	public async ValueTask<SharpObjectFlag?> Handle(GetObjectFlagQuery request,
		CancellationToken cancellationToken) =>
		await database.GetObjectFlagAsync(request.FlagName, cancellationToken);
}

public class GetObjectFlagsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetObjectFlagsQuery, SharpObjectFlag>
{
	public IAsyncEnumerable<SharpObjectFlag> Handle(GetObjectFlagsQuery request,
		CancellationToken cancellationToken) =>
		database.GetObjectFlagsAsync(request.Id, request.Type.ToUpper(), cancellationToken);
}