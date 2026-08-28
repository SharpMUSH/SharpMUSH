using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetHomedAtQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetHomedAtQuery, AnySharpContent>
{
	public IAsyncEnumerable<AnySharpContent> Handle(GetHomedAtQuery request, CancellationToken cancellationToken)
		=> database.GetHomedAtAsync(request.Home, cancellationToken);
}
