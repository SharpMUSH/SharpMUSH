using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The host's <see cref="IObjectRelationLoader"/>: relations resolve through the Mediator's cached
/// queries, so a provider-built object follows the same invalidation as every other read. Grants, owner,
/// location and parent, which every permission and lock check reads, and flags answer from <see cref="ObjectRelationMemo"/>
/// until the object is written.
/// </summary>
public class MediatorObjectRelationLoader(IMediator mediator, ObjectRelationMemo memo) : IObjectRelationLoader
{
	// Each relation asks the memo first and builds its reader only on a miss, in a method of its own: a lambda
	// capturing a parameter allocates its closure on entry to the method that holds it, hit or miss.
	public Task<AnySharpContainer> LocationOf(string typedId, string objectId, CancellationToken cancellationToken)
	{
		var number = int.Parse(objectId);
		return memo.KnownLocation(number) ?? ReadLocationAsync(typedId, objectId, number, cancellationToken);
	}

	private Task<AnySharpContainer> ReadLocationAsync(string typedId, string objectId, int number, CancellationToken cancellationToken)
		=> memo.LocationAsync(number, async ct => await mediator.Send(new GetCertainLocationQuery(typedId, objectId), ct),
			cancellationToken);

	/// <summary>Every object has a player for its owner; one that does not is a damaged world, and throws.</summary>
	public Task<SharpPlayer> OwnerOf(string objectId, int number, CancellationToken cancellationToken)
		=> memo.KnownOwner(number) ?? ReadOwnerOfAsync(objectId, number, cancellationToken);

	private Task<SharpPlayer> ReadOwnerOfAsync(string objectId, int number, CancellationToken cancellationToken)
		=> memo.OwnerAsync(number, ct => ReadOwnerAsync(objectId, number, ct), cancellationToken);

	private async Task<SharpPlayer> ReadOwnerAsync(string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetOwnerOfQuery(objectId, number), cancellationToken) switch
		{
			AnySharpObject and SharpPlayer owner => owner,
			AnySharpObject => throw new InvalidOperationException($"The owner of #{number} is not a player"),
			None => throw new InvalidOperationException($"No owner found for #{number}")
		};

	public Task<AnyOptionalSharpObject> ParentOf(string objectId, int number, CancellationToken cancellationToken)
		=> memo.KnownParent(number) ?? ReadParentAsync(objectId, number, cancellationToken);

	private Task<AnyOptionalSharpObject> ReadParentAsync(string objectId, int number, CancellationToken cancellationToken)
		=> memo.ParentAsync(number, async ct => await mediator.Send(new GetParentOfQuery(objectId, number), ct), cancellationToken);

	public async Task<AnyOptionalSharpObject> ZoneOf(string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetZoneOfQuery(objectId, number), cancellationToken);

	public async Task<AnySharpContainer> HomeOf(string typedId, string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetHomeOfQuery(typedId, objectId, number), cancellationToken);

	public async Task<AnyOptionalSharpContainer> DropToOf(string roomTypedId, string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetDropToOfQuery(roomTypedId, objectId, number), cancellationToken);

	public Task<Library.Authorization.ObjectGrants> GrantsOf(int number, bool isPlayer, CancellationToken cancellationToken)
		=> memo.KnownGrants(number, isPlayer) ?? ReadGrantsAsync(number, isPlayer, cancellationToken);

	private Task<Library.Authorization.ObjectGrants> ReadGrantsAsync(int number, bool isPlayer, CancellationToken cancellationToken)
		=> memo.GrantsAsync(number, isPlayer, async ct => await mediator.Send(new GetObjectGrantsQuery(number, isPlayer), ct),
			cancellationToken);

	public SharpObjectFlag[] FlagsOf(int number, string type, Func<SharpObjectFlag[]> read)
		=> memo.Flags(number, type, read);

	public async Task<AnyOptionalSharpContainer> ExitDestinationOf(string exitTypedId, string objectId, int number, CancellationToken cancellationToken)
		=> await mediator.Send(new GetExitDestinationOfQuery(exitTypedId, objectId, number), cancellationToken);
}
