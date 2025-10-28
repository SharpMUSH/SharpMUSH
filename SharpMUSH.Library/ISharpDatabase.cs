using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

public interface ISharpDatabase
{
	/// <summary>
	/// Initialize the Database, and Upgrade as needed.
	/// </summary>
	ValueTask Migrate(CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new player.
	/// </summary>
	/// <param name="name">Player name</param>
	/// <param name="password">Player password</param>
	/// <param name="location">Location to create it in</param>
	/// <returns>New player <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a hashed password for a player.
	/// </summary>
	/// <param name="player">Player</param>
	/// <param name="password">plaintext password</param>
	ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new room.
	/// </summary>
	/// <param name="name">Room Name</param>
	/// <param name="creator">Room Player-Creator</param>
	/// <returns>New room <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new thing.
	/// </summary>
	/// <param name="name">Thing name</param>
	/// <param name="location">Location to create it in</param>
	/// <param name="creator">Owner to the thing</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new exit.
	/// </summary>
	/// <param name="name">Exit name</param>
	/// <param name="aliases">Exit Aliases</param>
	/// <param name="location">Location for the Exit</param>
	/// <param name="creator">Owner to the exit</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator, CancellationToken cancellationToken = default);

	/// <summary>
	/// Link an exit to a destination location.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <param name="location"><see cref="AnySharpContainer"/></param>
	/// <returns>Success if all elements existed and were able to be linked</returns>
	ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unlink an exit from its destination location. Does not clear the DESTINATION / EXITTO attribute.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <returns>Success if the element still existed and could be unlinked.</returns>
	ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	/// <param name="lockString">The string of the Lock</param>
	ValueTask SetLockAsync(SharpObject target, string lockName, string lockString, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Unset the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get the attribute value of an object's attribute.
	/// </summary>
	/// <param name="dbref">DBRef of an object to get the attributes for</param>
	/// <param name="attribute">Attribute Path - uses attribute leaves</param>
	/// <returns>The <see cref="SharpAttribute"/> hierarchy, with the last attribute being the final leaf.</returns>
	ValueTask<IAsyncEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, CancellationToken cancellationToken = default, params string[] attribute);

	// TODO: Consider the return value, as an attribute pattern returns multiple attributes.
	// These should return full attribute paths, so likely IEnumerable<IEnumerable<SharpAttribute>>.
	ValueTask<IAsyncEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern, CancellationToken cancellationToken = default);
	ValueTask<IAsyncEnumerable<SharpAttribute>?> GetAttributesByRegexAsync(DBRef dbref, string attribute_pattern, CancellationToken cancellationToken = default);

	ValueTask<IAsyncEnumerable<LazySharpAttribute>?> GetLazyAttributeAsync(DBRef dbref, CancellationToken cancellationToken = default, params string[] attribute);
	ValueTask<IAsyncEnumerable<LazySharpAttribute>?> GetLazyAttributesAsync(DBRef dbref, string attribute_pattern, CancellationToken cancellationToken = default);
	ValueTask<IAsyncEnumerable<LazySharpAttribute>?> GetLazyAttributesByRegexAsync(DBRef dbref, string attribute_pattern, CancellationToken cancellationToken = default);
	IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync();

	/// <summary>
	/// Get the Object represented by a Database Reference Number.
	/// Optionally passing either the CreatedSecs or CreatedMilliseconds will do a more specific lookup.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <returns>A OneOf over the object being returned</returns>
	ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get an Object Flag by name, if it exists.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <returns>A SharpObjectFlag, or null if it does not exist</returns>
	ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all known Object Flags
	/// </summary>
	/// <returns>A list of all SharpObjectFlags</returns>
	ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an Object Flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Set an Object Power.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="power">Power</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Unset an Object Power.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="power">Power</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a name on an object.
	/// </summary>
	/// <param name="obj">The object to alter</param>
	/// <param name="value">The value for the field</param>
	/// <returns>Result</returns>
	ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Home of a content object. The 'home' of a Room is its Drop-To.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="home">New Value</param>
	ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Sets the Location of a content object. 
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="location">New Value</param>
	ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="parent">New Value</param>
	ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Owner of an Object to a player.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="owner">New Value</param>
	ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unset an Object flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <returns>The representing parent</returns>
	ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all powers the Server knows about.
	/// </summary>
	/// <returns>All powers</returns>
	ValueTask<IEnumerable<SharpPower>> GetObjectPowersAsync(CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <returns>The full representing parent chain</returns>
	ValueTask<IAsyncEnumerable<SharpObject>> GetParentsAsync(string id);

	ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default);

	ValueTask<IAsyncEnumerable<SharpPlayer>> GetPlayerByNameOrAliasAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="value">The value to place into the attribute</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="attr"></param>
	/// <param name="flag">Flag</param>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="flagName">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default);

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
	ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	/// <summary>
	/// Wipe an attribute and all of its children.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	ValueTask<IAsyncEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj);

	ValueTask<IAsyncEnumerable<AnySharpObject>> GetNearbyObjectsAsync(AnySharpObject obj);

	ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default);

	ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default);

	ValueTask<IAsyncEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj);

	ValueTask<IAsyncEnumerable<AnySharpContent>?> GetContentsAsync(AnySharpContainer node);

	ValueTask<IAsyncEnumerable<SharpExit>?> GetExitsAsync(DBRef obj);

	ValueTask<IAsyncEnumerable<SharpExit>> GetExitsAsync(AnySharpContainer node);

	ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="id">Location ID</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default);

	ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync(string id, CancellationToken cancellationToken = default);

	ValueTask<IEnumerable<SharpMail>> GetIncomingMailsAsync(SharpPlayer id, string folder);

	ValueTask<IEnumerable<SharpMail>> GetAllIncomingMailsAsync(SharpPlayer id);

	ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default);

	ValueTask<IEnumerable<SharpMail>> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient);
	
	ValueTask<IEnumerable<SharpMail>> GetAllSentMailsAsync(SharpObject sender);

	ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default);

	ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default);
	
	ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default);
	
	ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default);
	
	ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default);
	
	ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default);
	
	ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default);
		
	/// <summary>
	/// Sets expanded data for a SharpObject, that does not fit on the light-weight nature of a SharpObject or Attributes.
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the Expanded Object Data for a SharpObject. 
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<string?> GetExpandedObjectData(string sharpObjectId, string dataType, CancellationToken cancellationToken = default);


	/// <summary>
	/// Sets expanded data for a SharpObject, for the server as a whole.
	/// </summary>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	ValueTask SetExpandedServerData(string dataType, string data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the Expanded Object Data for the server as a whole. 
	/// </summary>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<string?> GetExpandedServerData(string dataType, CancellationToken cancellationToken = default);

	ValueTask<IEnumerable<SharpChannel>> GetAllChannelsAsync();
	
	ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default);
	
	ValueTask<IEnumerable<SharpChannel>> GetMemberChannelsAsync(AnySharpObject obj);

	ValueTask CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default);
	
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
		int? Buffer, CancellationToken cancellationToken = default);
	
	ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default);
	
	ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default);

	ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default);
	
	ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default);
	
	ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default);
}