using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeFlagsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributeFlagsQuery, SharpAttributeFlag>
{
	public IAsyncEnumerable<SharpAttributeFlag> Handle(GetAttributeFlagsQuery request,
		CancellationToken cancellationToken) =>
		database.GetAttributeFlagsAsync(cancellationToken);
}