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

public class GetHomeOfQueryHandler(ISharpDatabase database) : IQueryHandler<GetHomeOfQuery, AnySharpContainer>
{
	public async ValueTask<AnySharpContainer> Handle(GetHomeOfQuery query, CancellationToken cancellationToken)
		=> await database.GetHomeAsync(query.TypedId, cancellationToken);
}

public class GetDropToOfQueryHandler(ISharpDatabase database) : IQueryHandler<GetDropToOfQuery, AnyOptionalSharpContainer>
{
	public async ValueTask<AnyOptionalSharpContainer> Handle(GetDropToOfQuery query, CancellationToken cancellationToken)
		=> await database.GetDropToAsync(query.TypedId, cancellationToken);
}

public class GetExitDestinationOfQueryHandler(ISharpDatabase database) : IQueryHandler<GetExitDestinationOfQuery, AnyOptionalSharpContainer>
{
	public async ValueTask<AnyOptionalSharpContainer> Handle(GetExitDestinationOfQuery query, CancellationToken cancellationToken)
		=> await database.GetExitDestinationAsync(query.TypedId, cancellationToken);
}
