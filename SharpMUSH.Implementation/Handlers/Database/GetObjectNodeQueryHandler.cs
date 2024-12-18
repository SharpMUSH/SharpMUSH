using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectNodeQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetObjectNodeQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetObjectNodeQuery request, CancellationToken cancellationToken)
		=> await database.GetObjectNodeAsync(request.DBRef);
}

public class GetBaseObjectNodeQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetBaseObjectNodeQuery, SharpObject?>
{
	public async ValueTask<SharpObject?> Handle(GetBaseObjectNodeQuery request, CancellationToken cancellationToken)
		 => await database.GetBaseObjectNodeAsync(request.DBRef);
}
