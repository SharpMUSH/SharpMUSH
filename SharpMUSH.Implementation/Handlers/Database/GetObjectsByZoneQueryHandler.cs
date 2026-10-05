using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Streams the zone's member refs and resolves each through the object node cache, so a miss builds each
/// member once (see <see cref="GetContentsQueryHandler"/>).
/// </summary>
public class GetObjectsByZoneQueryHandler(INavigationStore database, IMediator mediator)
	: IStreamQueryHandler<GetObjectsByZoneQuery, SharpObject>
{
	public IAsyncEnumerable<SharpObject> Handle(GetObjectsByZoneQuery request, CancellationToken cancellationToken)
	{
		var zone = request.Zone switch
		{
			DBRef dbref => dbref,
			AnySharpObject known => known.Object().DBRef
		};

		return ObjectRefs.ResolveAsync<SharpObject>(mediator, database.GetZoneMemberRefsAsync(zone, cancellationToken),
			cancellationToken);
	}
}
