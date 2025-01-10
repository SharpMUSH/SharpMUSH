using MarkupString;
using SharpMUSH.Database.SurrealDB.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SurrealDb.Net;

namespace SharpMUSH.Database.SurrealDB;

/// <summary>
/// SurrealDB is not getting implemented until the library supports Transactions.
/// </summary>
/// <param name="Logger">Logger</param>
/// <param name="SDBC">Client</param>
/// <param name="PasswordService">Password Service</param>
public class SurrealDatabase(
	SurrealDbClient SDBC,
	IPasswordService PasswordService
) : ISharpDatabase
{
	public ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute)
	{
		throw new NotImplementedException();
	}

	public ValueTask<DBRef> CreateExitAsync(string name, string[] Aliases, AnySharpContainer location, SharpPlayer creator)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location)
	{
		throw new NotImplementedException();
	}

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var createdObject = await SDBC.Create(DatabaseConstants.objects,
			new SharpSurrealObjectCreateRequest(null, name, DatabaseConstants.typePlayer, [], time, time));
		var createdDBRef = new DBRef(createdObject.Id!.Value, time);

		await SDBC.Create(DatabaseConstants.players,
			new SharpSurrealPlayerCreateRequest(null, [], PasswordService.HashPassword(createdDBRef.ToString(), password)));

		return createdDBRef;
	}

	public ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator)
	{
		throw new NotImplementedException();
	}

	public ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute)
	{
		throw new NotImplementedException();
	}

	public ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync()
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern)
	{
		throw new NotImplementedException();
	}

	public ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(AnySharpContainer node)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpExit>> GetExitsAsync(AnySharpContainer node)
	{
		throw new NotImplementedException();
	}

	public ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1)
	{
		throw new NotImplementedException();
	}

	public ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1)
	{
		throw new NotImplementedException();
	}

	public ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(AnySharpObject obj)
	{
		throw new NotImplementedException();
	}

	public ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync()
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync(string id)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpMail>> GetIncomingMailsAsync(SharpPlayer id, string folder)
	{
		throw new NotImplementedException();
	}

	public ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpMail>> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient)
	{
		throw new NotImplementedException();
	}

	public ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpMail>> GetSentMailsAsync(SharpObject Sender, SharpObject Recipient)
	{
		throw new NotImplementedException();
	}

	public ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id)
	{
		throw new NotImplementedException();
	}

	public ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail)
	{
		throw new NotImplementedException();
	}

	public ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref)
	{
		throw new NotImplementedException();
	}

	public ValueTask<SharpObject?> GetParentAsync(string id)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpObject>> GetParentsAsync(string id)
	{
		throw new NotImplementedException();
	}

	public ValueTask<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name)
	{
		throw new NotImplementedException();
	}

	public ValueTask Migrate()
	{
		throw new NotImplementedException();
	}

	public ValueTask MoveObjectAsync(AnySharpContent enactorObj, DBRef destination)
	{
		throw new NotImplementedException();
	}

	public ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MarkupStringModule.MarkupString value, SharpPlayer owner)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag)
	{
		throw new NotImplementedException();
	}

	public ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag)
	{
		throw new NotImplementedException();
	}

	public ValueTask SetLockAsync(SharpObject target, string lockName, string lockString)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute)
	{
		throw new NotImplementedException();
	}
}