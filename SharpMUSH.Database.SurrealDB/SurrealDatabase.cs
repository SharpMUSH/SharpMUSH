using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SurrealDb.Net;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services;
using SharpMUSH.Database.SurrealDB.Models;

namespace SharpMUSH.Database.SurrealDB;

/// <summary>
/// SurrealDB is not getting implemented until the library supports Transactions.
/// </summary>
/// <param name="Logger">Logger</param>
/// <param name="SDBC">Client</param>
/// <param name="PasswordService">Password Service</param>
public class SurrealDatabase(
	ILogger<SurrealDatabase> Logger,
	SurrealDbClient SDBC,
	IPasswordService PasswordService
) : ISharpDatabase
{
	public Task<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync()
	{
		throw new NotImplementedException();
	}

	public Task<bool> ClearAttributeAsync(DBRef dbref, string[] attribute)
	{
		Logger.LogCritical("test");
		throw new NotImplementedException();
	}

	public Task<DBRef> CreateExitAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		throw new NotImplementedException();
	}

	public async Task<DBRef> CreatePlayerAsync(string name, string password, DBRef location)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var createdObject = await SDBC.Create(DatabaseConstants.objects, 
			new SharpSurrealObjectCreateRequest((int?)null, name, DatabaseConstants.typePlayer, [], time, time));
		var createdDBRef = new DBRef(createdObject.Id!.Value, time);

		var createdPlayer = await SDBC.Create(DatabaseConstants.players, 
			new SharpSurrealPlayerCreateRequest((int?)null, [], PasswordService.HashPassword(createdDBRef.ToString(), password)));

		return createdDBRef;
	}

	public Task<DBRef> CreateRoomAsync(string name, SharpPlayer creator)
	{
		throw new NotImplementedException();
	}

	public Task<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern)
	{
		throw new NotImplementedException();
	}

	public Task<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<AnySharpContent>?> GetContentsAsync(AnyOptionalSharpObject node)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<SharpExit>?> GetExitsAsync(AnyOptionalSharpContainer node)
	{
		throw new NotImplementedException();
	}

	public Task<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1)
	{
		throw new NotImplementedException();
	}

	public Task<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj)
	{
		throw new NotImplementedException();
	}

	public AnyOptionalSharpObject GetObjectNode(DBRef dbref)
	{
		throw new NotImplementedException();
	}

	public Task<SharpObjectFlag?> GetObjectFlagAsync(string name)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync()
	{
		throw new NotImplementedException();
	}

	public Task<bool> SetObjectFlagAsync(DBRef dbref, SharpObjectFlag flag)
	{
		throw new NotImplementedException();
	}

	public Task<bool> UnsetObjectFlagAsync(DBRef dbref, SharpObjectFlag flag)
	{
		throw new NotImplementedException();
	}

	public Task<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref)
	{
		throw new NotImplementedException();
	}

	public SharpObject? GetParent(string id)
	{
		throw new NotImplementedException();
	}

	public IEnumerable<SharpObject> GetParents(string id)
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name)
	{
		throw new NotImplementedException();
	}

	public Task Migrate()
	{
		throw new NotImplementedException();
	}

		public Task MoveObject(AnySharpContent enactorObj, DBRef destination)
		{
				throw new NotImplementedException();
		}

		public Task<bool> SetAttributeAsync(DBRef dbref, string[] attribute, string value, SharpPlayer owner)
	{
		throw new NotImplementedException();
	}

	public Task<bool> SetAttributeFlagAsync(DBRef dbref, string[] attribute, SharpAttributeFlag flag)
	{
		throw new NotImplementedException();
	}

	public Task<bool> UnsetAttributeFlagAsync(DBRef dbref, string[] attribute, SharpAttributeFlag flag)
	{
		throw new NotImplementedException();
	}

	public Task<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName)
	{
		throw new NotImplementedException();
	}

	public Task SetLockAsync(SharpObject target, string lockName, string lockString)
	{
		throw new NotImplementedException();
	}

	public Task<bool> WipeAttributeAsync(DBRef dbref, string[] attribute)
	{
		throw new NotImplementedException();
	}
}