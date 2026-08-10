using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Creating an object clears the cached lookup for the dbref it lands on.
/// </summary>
/// <remarks>
/// <see cref="SharpMUSH.Library.Queries.Database.GetObjectNodeByNumberQuery"/> is cacheable and the
/// caching behaviour stores misses too, so anything that looked up this dbref before it existed left
/// a "no such object" entry behind. The command's own <c>CacheKeys</c> cannot cover this: the new
/// dbref is not known until the insert returns, which is why the invalidation lives here.
/// </remarks>
public class CreateThingCommandHandler(ISharpDatabase database, IFusionCache cache)
	: ICommandHandler<CreateThingCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateThingCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreateThingAsync(request.Name, request.Where, request.Owner, request.Home, cancellationToken);
		await cache.RemoveAsync(CacheKeys.Object(created), token: cancellationToken);
		return created;
	}
}
