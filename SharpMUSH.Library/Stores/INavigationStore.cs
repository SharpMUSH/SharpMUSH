using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Graph traversal between objects: parents, owners, zones, homes, drop-tos, exits, contents, locations, and moves.
/// </summary>
public interface INavigationStore
{
	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The representing parent</returns>
	ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>The owner of the object with provider id <paramref name="id"/>. The uncached read behind <c>GetOwnerOfQuery</c>.</summary>
	ValueTask<SharpPlayer> GetObjectOwnerAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>The zone of the object with provider id <paramref name="id"/>, if any. The uncached read behind <c>GetZoneOfQuery</c>.</summary>
	ValueTask<AnyOptionalSharpObject> GetZoneAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>The home of the typed player or thing <paramref name="typedId"/>. The uncached read behind <c>GetHomeOfQuery</c>.</summary>
	ValueTask<AnySharpContainer> GetHomeAsync(string typedId, CancellationToken cancellationToken = default);

	/// <summary>The drop-to of the room <paramref name="roomTypedId"/>, if any. The uncached read behind <c>GetDropToOfQuery</c>.</summary>
	ValueTask<AnyOptionalSharpContainer> GetDropToAsync(string roomTypedId, CancellationToken cancellationToken = default);

	/// <summary>The destination of the exit <paramref name="exitTypedId"/>, if linked. The uncached read behind <c>GetExitDestinationOfQuery</c>.</summary>
	ValueTask<AnyOptionalSharpContainer> GetExitDestinationAsync(string exitTypedId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The full representing parent chain</returns>
	IAsyncEnumerable<SharpObject> GetParentsAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all exits that lead to a specific destination.
	/// </summary>
	/// <param name="destination">The destination DBRef</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of exits leading to the destination</returns>
	IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, CancellationToken cancellationToken = default);

	/// <summary>
	/// Everything whose <c>home</c> is <paramref name="home"/>: players and things that go there on
	/// <c>home</c>, plus exits that lead there (an exit's home edge <i>is</i> its destination).
	/// </summary>
	/// <remarks>
	/// Rooms are excluded. A room reuses the same home edge for its drop-to, which is not a home in
	/// any sense a caller here means. <see cref="GetEntrancesAsync"/> is the exit-only view of this.
	/// <para>
	/// Object destruction needs this: deleting an object severs the home edges pointing at it, and
	/// a player or thing with no home edge throws on every subsequent read, so those dependents have
	/// to be rehomed to <c>default_home</c> first — PennMUSH <c>free_object()</c>'s
	/// <c>Home(i) = DEFAULT_HOME</c> pass (<c>src/destroy.c</c>).
	/// </para>
	/// </remarks>
	/// <param name="home">The prospective home</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	IAsyncEnumerable<AnySharpContent> GetHomedAtAsync(DBRef home, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(DBRef obj, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(AnySharpObject obj, CancellationToken cancellationToken = default);

	ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default);

	ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node,
		CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, CancellationToken cancellationToken = default);

	ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="id">Location ID</param>
	/// <param name="depth">Depth</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The deepest findable object based on depth</returns>
	ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all objects that belong to a specific zone.
	/// </summary>
	/// <param name="zone">The zone object</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of all objects in the zone</returns>
	IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, CancellationToken cancellationToken = default);
}
