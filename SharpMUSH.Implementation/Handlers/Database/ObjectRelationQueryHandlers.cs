using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetOwnerOfQueryHandler(ISharpDatabase database) : IQueryHandler<GetOwnerOfQuery, SharpPlayer>
{
	public async ValueTask<SharpPlayer> Handle(GetOwnerOfQuery query, CancellationToken cancellationToken)
		=> await database.GetObjectOwnerAsync(query.Id, cancellationToken);
}

public class GetParentOfQueryHandler(ISharpDatabase database) : IQueryHandler<GetParentOfQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetParentOfQuery query, CancellationToken cancellationToken)
		=> await database.GetParentAsync(query.Id, cancellationToken);
}

public class GetZoneOfQueryHandler(ISharpDatabase database) : IQueryHandler<GetZoneOfQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetZoneOfQuery query, CancellationToken cancellationToken)
		=> await database.GetZoneAsync(query.Id, cancellationToken);
}
