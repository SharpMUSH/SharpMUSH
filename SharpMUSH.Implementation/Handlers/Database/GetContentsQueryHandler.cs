using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetContentsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetContentsQuery, AnySharpContent>
{
	public IAsyncEnumerable<AnySharpContent> Handle(GetContentsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Value switch
		{
			DBRef dbref => database.GetContentsAsync(dbref, cancellationToken),
			AnySharpContainer obj => database.GetContentsAsync(obj, cancellationToken),
			_ => throw new InvalidOperationException()
		};
}
