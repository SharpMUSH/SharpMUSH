using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>Materializes complete local attributes after validating the requested object identity.</summary>
public class GetListenAttributeSnapshotQueryHandler(IObjectStore objects, IAttributeStore attributes)
	: IQueryHandler<GetListenAttributeSnapshotQuery, SharpAttribute[]>
{
	public async ValueTask<SharpAttribute[]> Handle(GetListenAttributeSnapshotQuery request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (await objects.GetObjectNodeAsync(request.Object, cancellationToken) is not AnySharpObject)
			return [];
		return await attributes.GetAttributesAsync(request.Object, "**", cancellationToken).ToArrayAsync(cancellationToken);
	}
}
