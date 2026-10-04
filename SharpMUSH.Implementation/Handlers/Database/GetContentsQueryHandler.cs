using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Streams the container's content refs and resolves each through the object node cache. The caching
/// behaviour stores the list as those refs and resolves them again, which is then a hit: on a miss each
/// object is built once, by the node cache, not once here and again there (#1554).
/// </summary>
public class GetContentsQueryHandler(INavigationStore database, IMediator mediator)
	: IStreamQueryHandler<GetContentsQuery, AnySharpContent>
{
	public IAsyncEnumerable<AnySharpContent> Handle(GetContentsQuery request, CancellationToken cancellationToken)
	{
		var container = request.DBRef switch
		{
			DBRef dbref => dbref,
			AnySharpContainer known => known.Object().DBRef
		};

		return ObjectRefs.ResolveAsync<AnySharpContent>(mediator, database.GetContentRefsAsync(container, cancellationToken),
			cancellationToken);
	}
}
