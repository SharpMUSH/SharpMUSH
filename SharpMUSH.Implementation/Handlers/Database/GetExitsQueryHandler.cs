using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetExitsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetExitsQuery, SharpExit>
{
	public IAsyncEnumerable<SharpExit> Handle(GetExitsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Match(
			dbref     => database.GetExitsAsync(dbref, cancellationToken),
			container => database.GetExitsAsync(container, cancellationToken));
}
