using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The host's <see cref="IObjectRelationLoader"/>: relations resolve through the Mediator's cached
/// queries, so a provider-built object follows the same invalidation as every other read.
/// </summary>
public class MediatorObjectRelationLoader(IMediator mediator) : IObjectRelationLoader
{
	public async Task<AnySharpContainer> LocationOf(string typedId, string objectId, CancellationToken cancellationToken)
		=> await mediator.Send(new GetCertainLocationQuery(typedId, objectId), cancellationToken);

	public async Task<SharpPlayer> OwnerOf(string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetOwnerOfQuery(objectId, number), cancellationToken);

	public async Task<AnyOptionalSharpObject> ParentOf(string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetParentOfQuery(objectId, number), cancellationToken);

	public async Task<AnyOptionalSharpObject> ZoneOf(string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetZoneOfQuery(objectId, number), cancellationToken);

	public async Task<AnySharpContainer> HomeOf(string typedId, string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetHomeOfQuery(typedId, objectId, number), cancellationToken);

	public async Task<AnyOptionalSharpContainer> DropToOf(string roomTypedId, string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetDropToOfQuery(roomTypedId, objectId, number), cancellationToken);

	public async Task<AnyOptionalSharpContainer> ExitDestinationOf(string exitTypedId, string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetExitDestinationOfQuery(exitTypedId, objectId, number), cancellationToken);
}
