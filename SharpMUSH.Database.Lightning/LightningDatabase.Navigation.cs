using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="INavigationStore"/>: graph traversal between objects. Not ported yet — every member
/// throws <see cref="NotImplementedException"/> until Task 10.
/// </summary>
public sealed partial class LightningDatabase
{
	public ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpPlayer> GetObjectOwnerAsync(string id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnyOptionalSharpObject> GetZoneAsync(string id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnySharpContainer> GetHomeAsync(string typedId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnyOptionalSharpContainer> GetDropToAsync(string roomTypedId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnyOptionalSharpContainer> GetExitDestinationAsync(string exitTypedId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetParentsAsync(string id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AnySharpContent> GetHomedAtAsync(DBRef home, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
