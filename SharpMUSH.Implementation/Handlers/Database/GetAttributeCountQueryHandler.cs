using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeCountQueryHandler(IAttributeStore attributes) : IQueryHandler<GetAttributeCountQuery, long>
{
	public ValueTask<long> Handle(GetAttributeCountQuery request, CancellationToken cancellationToken)
		=> attributes.GetAttributeCountAsync(request.Object, cancellationToken);
}
