﻿using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

public interface ISharpDatabase
{
	ValueTask Migrate();

	ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location);

	ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator);

	ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator);

	ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator);

	ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location);

	ValueTask SetLockAsync(SharpObject target, string lockName, string lockString);

	ValueTask<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute);

	ValueTask<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern);

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
	/// Unset an Object flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag);

	ValueTask<SharpObject?> GetParentAsync(string id);

	ValueTask<IEnumerable<SharpObject>> GetParentsAsync(string id);

	ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref);

	ValueTask<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name);

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
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	ValueTask<bool> UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag);

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
	Task SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data);

	/// <summary>
	/// Gets the Expanded Object Data for a SharpObject. 
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<string?> GetExpandedObjectData(string sharpObjectId, string dataType);

}