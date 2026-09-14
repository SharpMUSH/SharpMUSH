using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>Creates a player and stamps the configured default player flags on it.</summary>
/// <param name="Salt">
/// <c>null</c> for a new player, whose <paramref name="Password"/> is plaintext and is hashed. Any other
/// value, empty included, stores <paramref name="Password"/> verbatim: how an import keeps a source
/// database's stored passwords (see <c>IObjectStore.CreatePlayerAsync</c>).
/// </param>
/// <param name="ApplyDefaultFlags">
/// Whether the configured default player flags are stamped on the new player. False for an import,
/// whose players are the source database's rather than new ones here.
/// </param>
/// <param name="CreationTime">
/// Creation time in Unix milliseconds, or <c>null</c> for now. An import passes the source's, since the
/// objid is <c>#N:&lt;creation time&gt;</c> and softcode in the imported database holds those.
/// </param>
/// <param name="ModifiedTime">Modification time in Unix milliseconds, or <c>null</c> to match the creation time.</param>
public record CreatePlayerCommand(string Name, string Password, DBRef Location, DBRef Home, int Quota, string? Salt = null,
	bool ApplyDefaultFlags = true, long? CreationTime = null, long? ModifiedTime = null) : ICommand<DBRef>, ICacheInvalidating, ICacheInvalidatingByResult<DBRef>
{
	public string[] CacheKeys => [Definitions.CacheKeys.Contents(Location)];

	public string[] CacheTags => [
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ObjectList,
		Definitions.CacheTags.PlayerList,
		Definitions.CacheTags.PlayerNames,
		Definitions.CacheKeys.ContentsTag(Location.Number)];

	/// <summary>The dbref the write allocated may have been resolved, and cached as missing, before it existed.</summary>
	public string[] CacheKeysFor(DBRef created) => [Definitions.CacheKeys.Object(created)];
}
