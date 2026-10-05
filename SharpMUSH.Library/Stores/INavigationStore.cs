using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Graph traversal between objects: parents, owners, zones, homes, drop-tos, exits, contents, locations, and moves.
/// </summary>
public interface INavigationStore
{
	/// <summary>
	/// The object <paramref name="subject"/>'s single <paramref name="relation"/> edge points at, as its full
	/// object id (number and creation milliseconds), or <see cref="NotFound"/> when the edge is absent or the
	/// object it names is gone. Reads the target's header only, not the whole object: the caller resolves the
	/// ref through the object node cache, so the object is built once (#1554). When <paramref name="subject"/>
	/// carries creation milliseconds that no longer match the stored object, the answer is
	/// <see cref="NotFound"/>.
	/// </summary>
	ValueTask<Found<DBRef>> GetRelationRefAsync(ObjectRelationKind relation, DBRef subject,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// The container <paramref name="subject"/> sits in, <paramref name="depth"/> location hops out (<c>-1</c>
	/// walking until there is no further edge, capped at 999; <c>0</c> taking no hop and answering
	/// <see cref="NotFound"/>), as its full object id. A chain that runs out of edges early answers with the
	/// last container it reached. <see cref="NotFound"/> when <paramref name="subject"/> is gone (or its
	/// creation milliseconds no longer match), has no location, or the container is gone. One read
	/// transaction, header decodes only.
	/// </summary>
	ValueTask<Found<DBRef>> GetLocationRefAsync(DBRef subject, int depth = 1, CancellationToken cancellationToken = default);

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

	/// <summary>
	/// The full object ids of what sits in <paramref name="container"/>, rooms excepted (a room's location edge
	/// is its drop-to), in dbref order. Paged: no read transaction is held between pages, and each entry costs
	/// a header decode, not a hydration; the caller resolves each ref through the object node cache.
	/// </summary>
	IAsyncEnumerable<DBRef> GetContentRefsAsync(DBRef container, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, CancellationToken cancellationToken = default);

	ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default);

	/// <summary>
	/// The full object ids of every object whose zone is <paramref name="zone"/>, in dbref order, paged and
	/// read from the headers only, like <see cref="GetContentRefsAsync"/>.
	/// </summary>
	IAsyncEnumerable<DBRef> GetZoneMemberRefsAsync(DBRef zone, CancellationToken cancellationToken = default);
}
