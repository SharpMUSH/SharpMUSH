using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <inheritdoc cref="CreateThingCommandHandler"/>
public class CreateExitCommandHandler(ISharpDatabase database, IFusionCache cache)
	: ICommandHandler<CreateExitCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateExitCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreateExitAsync(request.Name, request.Aliases, request.Location, request.Creator, cancellationToken);
		await cache.RemoveAsync(CacheKeys.Object(created), token: cancellationToken);
		return created;
	}
}
