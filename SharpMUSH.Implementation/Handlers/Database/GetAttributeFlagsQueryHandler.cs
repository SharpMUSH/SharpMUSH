using MediatR;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeFlagsQueryHandler(ISharpDatabase database)
	: IRequestHandler<GetAttributeFlagsQuery, IEnumerable<SharpAttributeFlag>>
{
	public async Task<IEnumerable<SharpAttributeFlag>> Handle(GetAttributeFlagsQuery request,
		CancellationToken cancellationToken)
	{
		return await database.GetAttributeFlagsAsync();
	}
}