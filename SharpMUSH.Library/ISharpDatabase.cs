using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

public interface ISharpDatabase
{
	/// <summary>
	/// Initialize the Database, and Upgrade as needed.
	/// </summary>
	ValueTask Migrate();

	/// <summary>
	/// Create a new player.
	/// </summary>
	/// <param name="name">Player name</param>
	/// <param name="password">Player password</param>
	/// <param name="location">Location to create it in</param>
	/// <returns>New player <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location);

	/// <summary>
	/// Sets a hashed password for a player.
	/// </summary>
	/// <param name="player">Player</param>
	/// <param name="password">plaintext password</param>
	ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password);

	/// <summary>
	/// Create a new room.
	/// </summary>
	/// <param name="name">Room Name</param>
	/// <param name="creator">Room Player-Creator</param>
	/// <returns>New room <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator);

	/// <summary>
	/// Create a new thing.
	/// </summary>
	/// <param name="name">Thing name</param>
	/// <param name="location">Location to create it in</param>
	/// <param name="creator">Owner to the thing</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator);

	/// <summary>
	/// Create a new exit.
	/// </summary>
	/// <param name="name">Exit name</param>
	/// <param name="aliases">Exit Aliases</param>
	/// <param name="location">Location for the Exit</param>
	/// <param name="creator">Owner to the exit</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator);

	/// <summary>
	/// Link an exit to a destination location.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <param name="location"><see cref="AnySharpContainer"/></param>
	/// <returns>Success if all elements existed and were able to be linked</returns>
	ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location);

	/// <summary>
	/// Unlink an exit from its destination location. Does not clear the DESTINATION / EXITTO attribute.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <returns>Success if the element still existed and could be unlinked.</returns>
	ValueTask<bool> UnlinkExitAsync(SharpExit exit);

	/// <summary>
	/// Set the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	/// <param name="lockString">The string of the Lock</param>
	ValueTask SetLockAsync(SharpObject target, string lockName, string lockString);
	
	/// <summary>
	/// Unset the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	ValueTask UnsetLockAsync(SharpObject target, string lockName);

	/// <summary>
	/// Get the attribute value of an object's attribute.
	/// </summary>
	/// <param name="dbref">DBRef of an object to get the attributes for</param>
	/// <param name="attribute">Attribute Path - uses attribute leaves</param>
	/// <returns>The <see cref="SharpAttribute"/> hierarchy, with the last attribute being the final leaf.</returns>
	ValueTask<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute);

	// TODO: Consider the return value, as an attribute pattern returns multiple attributes.
	// These should return full attribute paths, so likely IEnumerable<IEnumerable<SharpAttribute>>.
	ValueTask<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern);
	ValueTask<IEnumerable<SharpAttribute>?> GetAttributesByRegexAsync(DBRef dbref, string attribute_pattern);

	ValueTask<IEnumerable<LazySharpAttribute>?> GetLazyAttributeAsync(DBRef dbref, params string[] attribute);
	ValueTask<IEnumerable<LazySharpAttribute>?> GetLazyAttributesAsync(DBRef dbref, string attribute_pattern);
	ValueTask<IEnumerable<LazySharpAttribute>?> GetLazyAttributesByRegexAsync(DBRef dbref, string attribute_pattern);

	/// <summary>
	/// Get the Object represented by a Database Reference Number.
	/// Optionally passing either the CreatedSecs or CreatedMilliseconds will do a more specific lookup.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <returns>A OneOf over the object being returned</returns>
	ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref);

	/// <summary>
	/// Get an Object Flag by name, if it exists.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <returns>A SharpObjectFlag, or null if it does not exist</returns>
	ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name);

	/// <summary>
	/// Get all known Object Flags
	/// </summary>
	/// <returns>A list of all SharpObjectFlags</returns>
	ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync();

	/// <summary>
	/// Set an Object Flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag);

	/// <summary>
	/// Sets a name on an object.
	/// </summary>
	/// <param name="obj">The object to alter</param>
	/// <param name="value">The value for the field</param>
	/// <returns>Result</returns>
	ValueTask SetObjectName(AnySharpObject obj, MString value);

	/// <summary>
	/// Sets the Home of a content object. The 'home' of a Room is its Drop-To.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="home">New Value</param>
	ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home);
	
	/// <summary>
	/// Sets the Location of a content object. 
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="location">New Value</param>
	ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location);
	
	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="parent">New Value</param>
	ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent);
	
	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	ValueTask UnsetObjectParent(AnySharpObject obj);

	/// <summary>
	/// Sets the Owner of an Object to a player.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="owner">New Value</param>
	ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner);

	/// <summary>
	/// Unset an Object flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag);

	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <returns>The representing parent</returns>
	ValueTask<AnyOptionalSharpObject> GetParentAsync(string id);

	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <returns>The full representing parent chain</returns>
	ValueTask<IEnumerable<SharpObject>> GetParentsAsync(string id);

	ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref);

	ValueTask<IEnumerable<SharpPlayer>> GetPlayerByNameOrAliasAsync(string name);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="value">The value to place into the attribute</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="attr"></param>
	/// <param name="flag">Flag</param>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="flagName">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <returns>Success or Failure</returns>
	ValueTask<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync();

	/// <summary>
	/// Sets an attribute to string.Empty, or if it has no children, removes it entirely.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute);

	/// <summary>
	/// Wipe an attribute and all of its children.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute);

	ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj);

	ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(AnySharpObject obj);

	ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1);

	ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1);

	ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj);

	ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(AnySharpContainer node);

	ValueTask<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj);

	ValueTask<IEnumerable<SharpExit>> GetExitsAsync(AnySharpContainer node);

	ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination);

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="id">Location ID</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1);

	ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync(string id);

	ValueTask<IEnumerable<SharpMail>> GetIncomingMailsAsync(SharpPlayer id, string folder);

	ValueTask<IEnumerable<SharpMail>> GetAllIncomingMailsAsync(SharpPlayer id);

	ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail);

	ValueTask<IEnumerable<SharpMail>> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient);
	
	ValueTask<IEnumerable<SharpMail>> GetAllSentMailsAsync(SharpObject sender);

	ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail);

	ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id);
	
	ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail);
	
	ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail);
	
	ValueTask DeleteMailAsync(string mailId);
	
	ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder);
	
	ValueTask MoveMailFolderAsync(string mailId, string newFolder);
		
	/// <summary>
	/// Sets expanded data for a SharpObject, that does not fit on the light-weight nature of a SharpObject or Attributes.
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data);

	/// <summary>
	/// Gets the Expanded Object Data for a SharpObject. 
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<string?> GetExpandedObjectData(string sharpObjectId, string dataType);


	/// <summary>
	/// Sets expanded data for a SharpObject, for the server as a whole.
	/// </summary>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	ValueTask SetExpandedServerData(string dataType, string data);

	/// <summary>
	/// Gets the Expanded Object Data for the server as a whole. 
	/// </summary>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<string?> GetExpandedServerData(string dataType);

	ValueTask<IEnumerable<SharpChannel>> GetAllChannelsAsync();
	
	ValueTask<SharpChannel?> GetChannelAsync(string name);
	
	ValueTask<IEnumerable<SharpChannel>> GetMemberChannelsAsync(AnySharpObject obj);

	ValueTask CreateChannelAsync(MString name, string[] privs, SharpPlayer owner);
	
	ValueTask UpdateChannelAsync(SharpChannel channel,
		MString? Name,
		MString? Description,
		string[]? Privs,
		string? JoinLock,
		string? SpeakLock,
		string? SeeLock,
		string? HideLock,
		string? ModLock,
		string? Mogrifier,
		int? Buffer);
	
	ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner);
	
	ValueTask DeleteChannelAsync(SharpChannel channel);

	ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj);
	
	ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj);
	
	ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status);
}