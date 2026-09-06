using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetLocationQueryHandler(INavigationStore database)
	: IQueryHandler<GetLocationQuery, AnyOptionalSharpContainer>
{
	public async ValueTask<AnyOptionalSharpContainer> Handle(GetLocationQuery request, CancellationToken cancellationToken)
	{
		return await database.GetLocationAsync(request.DBRef, request.Depth, cancellationToken);
	}
}

public class GetCertainLocationQueryHandler(INavigationStore database)
	: IQueryHandler<GetCertainLocationQuery, AnySharpContainer>
{
	public async ValueTask<AnySharpContainer> Handle(GetCertainLocationQuery request, CancellationToken cancellationToken)
	{
		return await database.GetLocationAsync(request.Key, request.Depth, cancellationToken);
	}
}