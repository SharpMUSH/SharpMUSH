using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Rewrites an object's creation and modification times, and with them its objid. The importer uses
/// it on the seeded objects it reuses, which a running game has usually already read.
/// </summary>
/// <param name="Target">Object to restamp; only its number is used</param>
/// <param name="CreationTime">Creation time in Unix milliseconds</param>
/// <param name="ModifiedTime">Modification time in Unix milliseconds, or <c>null</c> to match <paramref name="CreationTime"/></param>
public record SetObjectTimestampsCommand(DBRef Target, long CreationTime, long? ModifiedTime = null)
	: ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target)];
	public string[] CacheTags => [];
}
