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
		// First try exact match
		var exactMatch = await database.GetObjectFlagAsync(request.FlagName, cancellationToken);
		if (exactMatch is not null)
		{
			return exactMatch;
		}

		// If no exact match, try partial match (prefix matching)
		// Get all flags and find the first one that matches as a prefix (case-insensitive)
		await foreach (var flag in database.GetObjectFlagsAsync(cancellationToken))
		{
			// Check if the flag name starts with the search term
			if (flag.Name.StartsWith(request.FlagName, StringComparison.InvariantCultureIgnoreCase))
			{
				return flag;
			}

			// Check aliases too
			if (flag.Aliases is not null &&
			    flag.Aliases.Any(alias =>
				    alias.StartsWith(request.FlagName, StringComparison.InvariantCultureIgnoreCase)))
			{
				return flag;
			}
		}

		return null;
	}
}

public class GetObjectFlagsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetObjectFlagsQuery, SharpObjectFlag>
{
	public IAsyncEnumerable<SharpObjectFlag> Handle(GetObjectFlagsQuery request,
		CancellationToken cancellationToken) =>
		database.GetObjectFlagsAsync(request.Id, request.Type.ToUpper(), cancellationToken);
}