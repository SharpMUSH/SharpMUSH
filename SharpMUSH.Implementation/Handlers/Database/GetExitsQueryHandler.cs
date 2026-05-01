using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetExitsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetExitsQuery, SharpExit>
{
	public IAsyncEnumerable<SharpExit> Handle(GetExitsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Value switch
		{
			DBRef dbref => database.GetExitsAsync(dbref, cancellationToken),
			AnySharpContainer obj => database.GetExitsAsync(obj, cancellationToken),
			_ => throw new InvalidOperationException()
		};
}
