using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetContentsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetContentsQuery, AnySharpContent>
{
	public IAsyncEnumerable<AnySharpContent> Handle(GetContentsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Match(
			dbref     => database.GetContentsAsync(dbref, cancellationToken),
			container => database.GetContentsAsync(container, cancellationToken));
}
