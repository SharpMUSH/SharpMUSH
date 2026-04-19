using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAllTypedObjectsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAllTypedObjectsQuery, AnySharpObject>
{
	public IAsyncEnumerable<AnySharpObject> Handle(GetAllTypedObjectsQuery request, CancellationToken cancellationToken)
		=> database.GetAllTypedObjectsAsync(cancellationToken);
}
