using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Object identity and structure: creation, deletion, lookup, links, locks, and the fields set directly on an object (name, home, location, parent, zone, owner, warnings).
/// </summary>
public interface IObjectStore
{
	/// <summary>
	/// Create a new player.
	/// </summary>
	/// <param name="name">Player name</param>
	/// <param name="password">Player password (plaintext for new players, or pre-hashed for imports)</param>
	/// <param name="location">Location to create it in</param>
	/// <param name="home"></param>
	/// <param name="quota">Initial quota for the player</param>
	/// <param name="salt">Optional salt for imported passwords (null for new players)</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New player <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
		string? salt = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a hashed password for a player.
	/// </summary>
	/// <param name="player">Player</param>
	/// <param name="password">plaintext password</param>
	/// <param name="salt">Optional salt for imported passwords (null for new passwords)</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the quota for a player.
	/// </summary>
	/// <param name="player">Player</param>
	/// <param name="quota">New quota amount</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the count of objects owned by a player.
	/// </summary>
	/// <param name="player">Player whose objects to count</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Number of objects owned by the player</returns>
	ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the total number of objects in the database — PennMUSH's <c>db_top</c>, reported by the
	/// INFO socket command. Counted in the store rather than by walking the objects, because INFO is
	/// answerable before login and is polled by MUD listing crawlers.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Number of objects in the database</returns>
	ValueTask<int> GetObjectCountAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new room.
	/// </summary>
	/// <param name="name">Room Name</param>
	/// <param name="creator">Room Player-Creator</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New room <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new thing.
	/// </summary>
	/// <param name="name">Thing name</param>
	/// <param name="location">Location to create it in</param>
	/// <param name="creator">Owner to the thing</param>
	/// <param name="home">Home location for the thing</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator, AnySharpContainer home, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new exit.
	/// </summary>
	/// <param name="name">Exit name</param>
	/// <param name="aliases">Exit Aliases</param>
	/// <param name="location">Location for the Exit</param>
	/// <param name="creator">Owner to the exit</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator, CancellationToken cancellationToken = default);

	/// <summary>
	/// Irrevocably remove an object from the database — the storage half of PennMUSH's
	/// <c>free_object()</c> (<c>src/destroy.c</c>).
	/// </summary>
	/// <remarks>
	/// This is a raw storage operation and performs <b>no</b> game-layer bookkeeping: it does not
	/// move contents home, relink exits, chown possessions, run <c>@adestroy</c>, or check
	/// permissions. Callers must run those first — <c>IObjectDestructionService.DestroyObjectAsync</c>
	/// is the only supported entry point for destroying a live object.
	/// <para>
	/// Removes the object document, its typed vertex, its whole attribute subtree, its expanded
	/// object data, any mail it received, and every edge incident to those vertices. Edges pointing
	/// <i>at</i> the object from elsewhere (another object's parent, zone, home, or location) are
	/// removed too, which leaves those references unset — matching <c>free_object()</c>'s
	/// <c>Zone(i) = NOTHING</c> / <c>Parent(i) = NOTHING</c> sweep.
	/// </para>
	/// </remarks>
	/// <param name="dbref">Object to delete</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns><c>true</c> if an object was deleted, <c>false</c> if no such object existed</returns>
	ValueTask<bool> DeleteObjectAsync(DBRef dbref, CancellationToken cancellationToken = default);

	/// <summary>
	/// Link an exit to a destination location.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <param name="location"><see cref="AnySharpContainer"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if all elements existed and were able to be linked</returns>
	ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unlink an exit from its destination location. Does not clear the DESTINATION / EXITTO attribute.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if the element still existed and could be unlinked.</returns>
	ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default);

	/// <summary>
	/// Link a room to a location (drop-to).
	/// </summary>
	/// <param name="room"><see cref="SharpRoom"/></param>
	/// <param name="location"><see cref="AnyOptionalSharpContainer"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if all elements existed and were able to be linked</returns>
	ValueTask<bool> LinkRoomAsync(SharpRoom room, AnyOptionalSharpContainer location, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unlink a room from its location (drop-to).
	/// </summary>
	/// <param name="room"><see cref="SharpRoom"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if the element still existed and could be unlinked.</returns>
	ValueTask<bool> UnlinkRoomAsync(SharpRoom room, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	/// <param name="lockData">The lock data including string and flags</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetLockAsync(SharpObject target, string lockName, Models.SharpLockData lockData, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unset the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get the Object represented by a Database Reference Number.
	/// Optionally passing either the CreatedSecs or CreatedMilliseconds will do a more specific lookup.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A OneOf over the object being returned</returns>
	ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a name on an object.
	/// </summary>
	/// <param name="obj">The object to alter</param>
	/// <param name="value">The value for the field</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Result</returns>
	ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Home of a content object. The 'home' of a Room is its Drop-To.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="home">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Location of a content object. 
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="location">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="parent">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Zone of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="zone">New Zone</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unsets the Zone of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetObjectZone(AnySharpObject obj, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Owner of an Object to a player.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="owner">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the warning type flags for an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="warnings">Warning type flags</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectWarnings(AnySharpObject obj, WarningType warnings, CancellationToken cancellationToken = default);

	ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all objects in the database as a streaming AsyncEnumerable.
	/// This allows for efficient filtering and searching without loading all objects into memory.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of all SharpObjects in the database</returns>
	IAsyncEnumerable<SharpObject> GetAllObjectsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all objects in the database as fully-typed <see cref="AnySharpObject"/> instances.
	/// Use this instead of <see cref="GetAllObjectsAsync"/> when callers need the typed object
	/// without a subsequent per-object mediator query — which would cause FusionCache lock
	/// contention with concurrently-executing player commands.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of all fully-typed objects in the database</returns>
	IAsyncEnumerable<AnySharpObject> GetAllTypedObjectsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Get objects from the database with filtering applied at the database level.
	/// This is more efficient than loading all objects and filtering in application code.
	/// Lock evaluation must happen in application code, but other filters can be pushed to the database.
	/// </summary>
	/// <remarks>
	/// <b>Every</b> populated field of <paramref name="filter"/> must be honoured, and they compose as
	/// AND. A provider may not quietly drop a predicate it has not implemented: the call would succeed
	/// and hand back a set the caller never asked for. All three providers had done exactly that —
	/// SurrealDB and Memgraph ignored <c>Owner</c>/<c>Zone</c>/<c>Parent</c>/<c>HasFlag</c>/
	/// <c>HasPower</c> outright (returning the whole database), while ArangoDB's <c>Owner</c> compared
	/// a dbref against a generated typed-vertex key and its <c>HasFlag</c>/<c>HasPower</c> read array
	/// fields that objects do not have (returning nothing). Prefer throwing over silently widening.
	/// <para>
	/// Predicates whose meaning is not self-evident from the field name, defined so the three providers
	/// agree with each other <i>and</i> with the application-layer helpers callers compare against:
	/// </para>
	/// <list type="bullet">
	///   <item>
	///     <c>Owner</c>, <c>Zone</c>, <c>Parent</c> — match on the <b>dbref number</b> of the object the
	///     relationship resolves to. Note this is not always the key of the vertex the edge lands on:
	///     ownership points at the typed player vertex, which needs a further hop to its object.
	///   </item>
	///   <item>
	///     <c>HasFlag</c> — case-insensitive match on a flag's <c>Name</c> or any of its
	///     <c>Aliases</c>, <b>or</b> on the object's own <c>Type</c>. Type counts because
	///     <c>GetObjectFlagsAsync</c> synthesises a type-named flag that has no edge behind it, so
	///     <c>HasFlag("THING")</c> is true in application code (<c>HelperFunctions.HasFlag</c>) and has
	///     to be true here too. Aliases count because PennMUSH's <c>ptab_flag</c> holds them alongside
	///     the names, so <c>has_flag_by_name</c> resolves either spelling and that helper now does too.
	///   </item>
	///   <item>
	///     <c>HasPower</c> — case-insensitive match on a power's <c>Name</c> <b>or</b> its <c>Alias</c>,
	///     mirroring <c>HelperFunctions.HasPower</c>. There is no synthesised type-power.
	///   </item>
	/// </list>
	/// <para>
	/// <c>ObjectSearchFilterPushdownTests</c> pins each predicate against ground truth on every
	/// provider leg, asserting both that a matching object is returned and that a non-matching control
	/// is not — either half alone passes for one of the two failure modes above.
	/// </para>
	/// </remarks>
	/// <param name="filter">Filter criteria to apply at database level</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of filtered SharpObjects</returns>
	IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets all players in the database as a streaming AsyncEnumerable.
	/// This allows for efficient processing of all players without loading them all into memory.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of all SharpPlayers in the database</returns>
	IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Checks if there is a path from startObject to targetObject following parent and/or zone edges.
	/// Uses graph traversal to detect potential cycles in combined parent/zone chains.
	/// </summary>
	/// <param name="startObject">Starting object for traversal</param>
	/// <param name="targetObject">Target object to find</param>
	/// <param name="maxDepth">Maximum depth for traversal (default 100)</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if a path exists from start to target, false otherwise</returns>
	ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject, int maxDepth = 100, CancellationToken cancellationToken = default);
}
