using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeFlagsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributeFlagsQuery, IAsyncEnumerable<SharpAttributeFlag>>
{
	public async ValueTask<IAsyncEnumerable<SharpAttributeFlag>> Handle(GetAttributeFlagsQuery request,
		CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetAttributeFlagsAsync(cancellationToken);
	}
}