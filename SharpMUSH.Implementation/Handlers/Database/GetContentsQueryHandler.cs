using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetContentsQueryHandler(INavigationStore database)
	: IStreamQueryHandler<GetContentsQuery, AnySharpContent>
{
	public IAsyncEnumerable<AnySharpContent> Handle(GetContentsQuery request, CancellationToken cancellationToken)
		=> request.DBRef switch
		{
			DBRef dbref => database.GetContentsAsync(dbref, cancellationToken),
			AnySharpContainer container => database.GetContentsAsync(container, cancellationToken)
		};
}