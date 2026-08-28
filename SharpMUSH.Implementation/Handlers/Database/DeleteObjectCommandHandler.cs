using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Deleting an object clears the cached lookup for the dbref it vacated.
/// </summary>
/// <remarks>
/// Mirrors <see cref="CreateThingCommandHandler"/> from the other direction: the caching behaviour
/// stores hits, so anything that resolved this dbref before the delete left a live object behind in
/// the cache. The command's own <c>CacheKeys</c> run through the pipeline behaviour, but this
/// removal is repeated here with <see cref="CancellationToken.None"/> so a token cancelled between
/// the commit and the invalidation cannot leave a resolvable handle to a deleted object.
/// </remarks>
public class DeleteObjectCommandHandler(ISharpDatabase database, IFusionCache cache)
	: ICommandHandler<DeleteObjectCommand, bool>
{
	public async ValueTask<bool> Handle(DeleteObjectCommand request, CancellationToken cancellationToken)
	{
		var deleted = await database.DeleteObjectAsync(request.Target, cancellationToken);

		if (deleted)
		{
			await cache.RemoveAsync(CacheKeys.Object(request.Target), token: CancellationToken.None);
			await cache.RemoveAsync(CacheKeys.Contents(request.Target), token: CancellationToken.None);
		}

		return deleted;
	}
}
