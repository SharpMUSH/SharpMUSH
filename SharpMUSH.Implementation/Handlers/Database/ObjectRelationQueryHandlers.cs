using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

// Each relation handler reads the related object's ref (a header decode) and resolves it through the
// object node cache, so a miss builds the related object once, by that cache, and the caching
// behaviour's re-resolve of the stored ref is a hit (#1554).

public class GetOwnerOfQueryHandler(INavigationStore database, IMediator mediator) : IQueryHandler<GetOwnerOfQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetOwnerOfQuery query, CancellationToken cancellationToken)
		=> await ObjectRefs.NodeAsync(mediator,
			await database.GetRelationRefAsync(ObjectRelationKind.Owner, new DBRef(query.Number), cancellationToken), cancellationToken);
}

public class GetParentOfQueryHandler(INavigationStore database, IMediator mediator) : IQueryHandler<GetParentOfQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetParentOfQuery query, CancellationToken cancellationToken)
		=> await ObjectRefs.NodeAsync(mediator,
			await database.GetRelationRefAsync(ObjectRelationKind.Parent, new DBRef(query.Number), cancellationToken), cancellationToken);
}

public class GetZoneOfQueryHandler(INavigationStore database, IMediator mediator) : IQueryHandler<GetZoneOfQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetZoneOfQuery query, CancellationToken cancellationToken)
		=> await ObjectRefs.NodeAsync(mediator,
			await database.GetRelationRefAsync(ObjectRelationKind.Zone, new DBRef(query.Number), cancellationToken), cancellationToken);
}

public class GetHomeOfQueryHandler(INavigationStore database, IMediator mediator) : IQueryHandler<GetHomeOfQuery, AnySharpContainer>
{
	public async ValueTask<AnySharpContainer> Handle(GetHomeOfQuery query, CancellationToken cancellationToken)
		=> await HomeEdge.ReadAsync(database, mediator, query.Number, cancellationToken) switch
		{
			AnySharpObject home => ObjectRefs.EdgeContainer(home),
			None => throw new InvalidOperationException($"No home found for #{query.Number}")
		};
}

public class GetDropToOfQueryHandler(INavigationStore database, IMediator mediator) : IQueryHandler<GetDropToOfQuery, AnyOptionalSharpContainer>
{
	public async ValueTask<AnyOptionalSharpContainer> Handle(GetDropToOfQuery query, CancellationToken cancellationToken)
		=> HomeEdge.Optional(await HomeEdge.ReadAsync(database, mediator, query.Number, cancellationToken));
}

public class GetExitDestinationOfQueryHandler(INavigationStore database, IMediator mediator) : IQueryHandler<GetExitDestinationOfQuery, AnyOptionalSharpContainer>
{
	public async ValueTask<AnyOptionalSharpContainer> Handle(GetExitDestinationOfQuery query, CancellationToken cancellationToken)
		=> HomeEdge.Optional(await HomeEdge.ReadAsync(database, mediator, query.Number, cancellationToken));
}

/// <summary>
/// A home, a room's drop-to and an exit's destination are one edge; they differ only in whether its absence
/// is an error (a player or thing always has a home) or an answer (an unset drop-to, an unlinked exit).
/// </summary>
internal static class HomeEdge
{
	public static async ValueTask<AnyOptionalSharpObject> ReadAsync(INavigationStore database, IMediator mediator, int number,
		CancellationToken cancellationToken)
		=> await ObjectRefs.NodeAsync(mediator,
			await database.GetRelationRefAsync(ObjectRelationKind.Home, new DBRef(number), cancellationToken), cancellationToken);

	/// <remarks><see cref="ObjectRefs.EdgeContainer"/> throws for an exit, which no edge may name as a container.</remarks>
	public static AnyOptionalSharpContainer Optional(AnyOptionalSharpObject node) => node switch
	{
		AnySharpObject found => ObjectRefs.EdgeContainer(found).WithNoneOption(),
		None none => none
	};
}
