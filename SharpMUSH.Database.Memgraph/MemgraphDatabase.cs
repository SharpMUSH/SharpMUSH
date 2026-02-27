using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using MString = MarkupString.MarkupStringModule.MarkupString;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase(
	ILogger<MemgraphDatabase> logger,
	IDriver driver,
	IPasswordService passwordService
) : ISharpDatabase
{
	public async ValueTask Migrate(CancellationToken cancellationToken = default)
	{
		try
		{
			logger.LogInformation("Migrating Memgraph Database");

			// Create uniqueness constraint on Object.key
			await driver
				.ExecutableQuery("CREATE CONSTRAINT ON (o:Object) ASSERT o.key IS UNIQUE")
				.ExecuteAsync(cancellationToken);

			// Create index on Object.type for faster lookups
			await driver
				.ExecutableQuery("CREATE INDEX ON :Object(type)")
				.ExecuteAsync(cancellationToken);

			// Create index on Object.name for player lookups
			await driver
				.ExecutableQuery("CREATE INDEX ON :Object(name)")
				.ExecuteAsync(cancellationToken);

			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			// Create Room Zero (key=0)
			await driver
				.ExecutableQuery("""
					MERGE (o:Object {key: 0})
					ON CREATE SET o.name = 'Room Zero',
						o.type = 'ROOM',
						o.creationTime = $now,
						o.modifiedTime = $now
					MERGE (r:Room {key: 0})
					MERGE (r)-[:IS_OBJECT]->(o)
					""")
				.WithParameters(new { now })
				.ExecuteAsync(cancellationToken);

			// Create Player One - God (key=1)
			var passwordHash = passwordService.HashPassword("#1:" + now, "potrzebie");
			await driver
				.ExecutableQuery("""
					MERGE (o:Object {key: 1})
					ON CREATE SET o.name = 'God',
						o.type = 'PLAYER',
						o.creationTime = $now,
						o.modifiedTime = $now
					MERGE (p:Player {key: 1})
					ON CREATE SET p.passwordHash = $passwordHash,
						p.passwordSalt = null,
						p.aliases = [],
						p.quota = 999999
					MERGE (p)-[:IS_OBJECT]->(o)
					""")
				.WithParameters(new { now, passwordHash })
				.ExecuteAsync(cancellationToken);

			// Create Room Two - Master Room (key=2)
			await driver
				.ExecutableQuery("""
					MERGE (o:Object {key: 2})
					ON CREATE SET o.name = 'Master Room',
						o.type = 'ROOM',
						o.creationTime = $now,
						o.modifiedTime = $now
					MERGE (r:Room {key: 2})
					MERGE (r)-[:IS_OBJECT]->(o)
					""")
				.WithParameters(new { now })
				.ExecuteAsync(cancellationToken);

			// Player One is located at Room Zero
			await driver
				.ExecutableQuery("""
					MATCH (p:Player {key: 1}), (r:Room {key: 0})
					MERGE (p)-[:AT_LOCATION]->(r)
					""")
				.ExecuteAsync(cancellationToken);

			// Player One's home is Room Zero
			await driver
				.ExecutableQuery("""
					MATCH (p:Player {key: 1}), (r:Room {key: 0})
					MERGE (p)-[:HAS_HOME]->(r)
					""")
				.ExecuteAsync(cancellationToken);

			// Player One owns Room Zero, Player One, and Room Two
			await driver
				.ExecutableQuery("""
					MATCH (owner:Object {key: 1})
					MATCH (o:Object) WHERE o.key IN [0, 1, 2]
					MERGE (o)-[:HAS_OWNER]->(owner)
					""")
				.ExecuteAsync(cancellationToken);

			logger.LogInformation("Memgraph Migration Completed");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Memgraph Migration Failed");
			throw;
		}
	}

	public ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
		string? salt = null, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator, AnySharpContainer home, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator, CancellationToken cancellationToken = default)
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

	public IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttribute> GetAttributesAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttribute> GetAttributesByRegexAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesByRegexAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(DBRef dbref, string[] attribute, bool checkParent = true, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(DBRef dbref, string[] attribute, bool checkParent = true, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags, string? limit = null, string[]? enumValues = null, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol, bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UpdatePowerAsync(string name, string alias, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
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

	public ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetParentsAsync(string id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetAllObjectsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
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

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(string id, string type, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetAllSystemMailAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpChannel> GetAllChannelsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateChannelAsync(SharpChannel channel, MString? name, MString? description, string[]? privs,
		string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock,
		string? mogrifier, int? buffer, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject, int maxDepth = 100, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
