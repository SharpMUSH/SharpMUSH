using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeFlagsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributeFlagsQuery, IEnumerable<SharpAttributeFlag>>
{
	public async ValueTask<IEnumerable<SharpAttributeFlag>> Handle(GetAttributeFlagsQuery request,
		CancellationToken cancellationToken)
	{
		return await database.GetAttributeFlagsAsync(cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
	}
}