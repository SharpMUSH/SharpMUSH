using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAccountHolderQueryHandler(IAccountHolderStore store) : IQueryHandler<GetAccountHolderQuery, Found<SharpAccountHolder>>
{
	public ValueTask<Found<SharpAccountHolder>> Handle(GetAccountHolderQuery request, CancellationToken cancellationToken)
		=> store.GetAccountHolderAsync(request.Holder, cancellationToken);
}
