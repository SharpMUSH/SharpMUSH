using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetLocationQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetLocationQuery, AnyOptionalSharpContainer>
{
	public async ValueTask<AnyOptionalSharpContainer> Handle(GetLocationQuery request, CancellationToken cancellationToken)
	{
		return await database.GetLocationAsync(request.DBRef, request.Depth);
	}
}

public class GetCertainLocationQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetCertainLocationQuery, AnySharpContainer>
{
	public async ValueTask<AnySharpContainer> Handle(GetCertainLocationQuery request, CancellationToken cancellationToken)
	{
		return await database.GetLocationAsync(request.Key, request.Depth);
	}
}