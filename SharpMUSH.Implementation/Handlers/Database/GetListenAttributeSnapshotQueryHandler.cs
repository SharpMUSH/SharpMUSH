using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>Materializes one object's complete attribute set through its Mediator-backed node.</summary>
public class GetListenAttributeSnapshotQueryHandler(IMediator mediator)
	: IQueryHandler<GetListenAttributeSnapshotQuery, SharpAttribute[]>
{
	public async ValueTask<SharpAttribute[]> Handle(GetListenAttributeSnapshotQuery request, CancellationToken cancellationToken)
		=> await mediator.Send(new GetObjectNodeQuery(request.Object), cancellationToken) is AnySharpObject obj
			? await obj.Object().AllAttributes.Value.ToArrayAsync(cancellationToken)
			: [];
}
