using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectFlagsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetObjectFlagsQuery, IEnumerable<SharpObjectFlag>>
{
	public async ValueTask<IEnumerable<SharpObjectFlag>> Handle(GetObjectFlagsQuery request,
		CancellationToken cancellationToken)
	{
		return await database.GetObjectFlagsAsync();
	}
}

public class GetObjectFlagQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetObjectFlagQuery, SharpObjectFlag?>
{
	public async ValueTask<SharpObjectFlag?> Handle(GetObjectFlagQuery request,
		CancellationToken cancellationToken)
	{
		return await database.GetObjectFlagAsync(request.FlagName);
	}
}