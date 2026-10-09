using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Reads the container's ref (header decodes only) and resolves it through the object node cache, so a miss
/// builds the container once (#1554). <see cref="ObjectRefs.EdgeContainer"/> throws for an exit, which cannot be a location.
/// </summary>
public class GetLocationQueryHandler(INavigationStore database, IMediator mediator)
	: IQueryHandler<GetLocationQuery, AnyOptionalSharpContainer>
{
	public async ValueTask<AnyOptionalSharpContainer> Handle(GetLocationQuery request, CancellationToken cancellationToken)
		=> await ObjectRefs.NodeAsync(mediator,
				await database.GetLocationRefAsync(request.DBRef, request.Depth, cancellationToken), cancellationToken) switch
		{
			AnySharpObject location => ObjectRefs.EdgeContainer(location).WithNoneOption(),
			None none => none
		};
}

public class GetCertainLocationQueryHandler(INavigationStore database, IMediator mediator)
	: IQueryHandler<GetCertainLocationQuery, AnySharpContainer>
{
	public async ValueTask<AnySharpContainer> Handle(GetCertainLocationQuery request, CancellationToken cancellationToken)
	{
		// ObjectId is the base object's id, its dbref number: the one identity per object (see the query).
		var subject = new DBRef(int.Parse(request.ObjectId));
		return await ObjectRefs.NodeAsync(mediator,
				await database.GetLocationRefAsync(subject, request.Depth, cancellationToken), cancellationToken) switch
		{
			AnySharpObject location => ObjectRefs.EdgeContainer(location),
			None => throw new InvalidOperationException($"No location found for #{subject.Number}")
		};
	}
}
