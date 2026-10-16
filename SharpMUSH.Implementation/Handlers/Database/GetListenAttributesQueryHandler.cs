using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>Compiles visible local patterns; inherited searches retain complete snapshots separately.</summary>
public class GetListenAttributesQueryHandler(IAttributeStore store) : IQueryHandler<GetListenAttributesQuery, ListenAttributeCache[]>
{
	public async ValueTask<ListenAttributeCache[]> Handle(GetListenAttributesQuery request, CancellationToken cancellationToken)
	{
		var attributes = await store.GetDiscoveryAttributesAsync(request.SharpObject.Object().DBRef, cancellationToken).ToArrayAsync(cancellationToken);
		return ListenAttributeSearch.Compile(new ListenAttributeSearch().Visible(attributes, false, cancellationToken), cancellationToken);
	}
}
