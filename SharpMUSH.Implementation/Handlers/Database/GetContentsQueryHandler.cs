using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetContentsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetContentsQuery, AnySharpContent>
{
	public IAsyncEnumerable<AnySharpContent> Handle(GetContentsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Match<IAsyncEnumerable<AnySharpContent>>(
			dbref => database.GetContentsAsync(dbref, cancellationToken),
			obj => database.GetContentsAsync(obj, cancellationToken));
}