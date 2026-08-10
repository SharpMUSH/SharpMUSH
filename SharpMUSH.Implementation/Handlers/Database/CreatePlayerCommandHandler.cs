using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <inheritdoc cref="CreateThingCommandHandler"/>
public class CreatePlayerCommandHandler(ISharpDatabase database, IFusionCache cache)
	: ICommandHandler<CreatePlayerCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreatePlayerCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreatePlayerAsync(
			request.Name, request.Password, request.Location, request.Home, request.Quota, request.Salt, cancellationToken);
		await cache.RemoveAsync(CacheKeys.Object(created), token: cancellationToken);
		return created;
	}
}
