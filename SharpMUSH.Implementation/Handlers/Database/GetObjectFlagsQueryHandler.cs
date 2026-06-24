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
		CancellationToken cancellationToken)
	{
		var exactMatch = await database.GetObjectFlagAsync(request.FlagName, cancellationToken);
		if (exactMatch is not null)
		{
			return exactMatch;
		}

		return await database.GetObjectFlagsAsync(cancellationToken)
			.FirstOrDefaultAsync(
				flag => flag.Name.StartsWith(request.FlagName, StringComparison.InvariantCultureIgnoreCase)
					|| (flag.Aliases?.Any(alias =>
						alias.StartsWith(request.FlagName, StringComparison.InvariantCultureIgnoreCase)) ?? false),
				cancellationToken);
	}
}

public class GetObjectFlagsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetObjectFlagsQuery, SharpObjectFlag>
{
	public IAsyncEnumerable<SharpObjectFlag> Handle(GetObjectFlagsQuery request,
		CancellationToken cancellationToken) =>
		database.GetObjectFlagsAsync(request.Id, request.Type.ToUpper(), cancellationToken);
}