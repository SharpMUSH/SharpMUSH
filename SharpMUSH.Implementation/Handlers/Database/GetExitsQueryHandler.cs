using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetExitsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetExitsQuery, SharpExit>
{
	public IAsyncEnumerable<SharpExit> Handle(GetExitsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Match<IAsyncEnumerable<SharpExit>?>(
			   dbref => database.GetExitsAsync(dbref, cancellationToken)
				   .AsTask().GetAwaiter().GetResult(),
			   obj => database.GetExitsAsync(obj, cancellationToken)
				   .AsTask().GetAwaiter().GetResult())
		   ?? AsyncEnumerable.Empty<SharpExit>();
}