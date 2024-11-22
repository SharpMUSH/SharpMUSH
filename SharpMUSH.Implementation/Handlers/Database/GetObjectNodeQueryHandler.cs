using MediatR;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectNodeQueryHandler(ISharpDatabase database)
	: IRequestHandler<GetObjectNodeQuery, AnyOptionalSharpObject>
{
	public async Task<AnyOptionalSharpObject> Handle(GetObjectNodeQuery request, CancellationToken cancellationToken)
	{
		return await database.GetObjectNodeAsync(request.DBRef);
	}
}