using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IObjectStore"/>: object identity and structure. Not ported yet — every member throws
/// <see cref="NotImplementedException"/> until Task 7.
/// </summary>
public sealed partial class LightningDatabase
{
	public ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
		string? salt = null, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<int> GetObjectCountAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator, AnySharpContainer home, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> DeleteObjectAsync(DBRef dbref, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> LinkRoomAsync(SharpRoom room, AnyOptionalSharpContainer location, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UnlinkRoomAsync(SharpRoom room, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetLockAsync(SharpObject target, string lockName, SharpLockData lockData, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UnsetObjectZone(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetObjectWarnings(AnySharpObject obj, WarningType warnings, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetAllObjectsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AnySharpObject> GetAllTypedObjectsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject, int maxDepth = 100, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
