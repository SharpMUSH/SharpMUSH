using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

public interface ISharpDatabase
{
	Task Migrate();

	Task<DBRef> CreatePlayerAsync(string name, string password, DBRef location);

	Task<DBRef> CreateRoomAsync(string name, SharpPlayer creator);

	Task<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator);

	Task<DBRef> CreateExitAsync(string name, AnySharpContainer location, SharpPlayer creator);

	Task SetLockAsync(SharpObject target, string lockName, string lockString);

	Task<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute);
	
	Task<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern);

	/// <summary>
	/// Get the Object represented by a Database Reference Number.
	/// Optionally passing either the CreatedSecs or CreatedMilliseconds will do a more specific lookup.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <returns>A OneOf over the object being returned</returns>
	Task<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref);

	AnyOptionalSharpObject GetObjectNode(DBRef dbref);

	/// <summary>
	/// Get an Object Flag by name, if it exists.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <returns>A SharpObjectFlag, or null if it does not exist</returns>
	Task<SharpObjectFlag?> GetObjectFlagAsync(string name);
	
	/// <summary>
	/// Get all known Object Flags
	/// </summary>
	/// <returns>A list of all SharpObjectFlags</returns>
	Task<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync();


	/// <summary>
	/// Set an Object Flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	Task<bool> SetObjectFlagAsync(DBRef dbref, SharpObjectFlag flag);

	/// <summary>
	/// Unset an Object flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	Task<bool> UnsetObjectFlagAsync(DBRef dbref, SharpObjectFlag flag);

	
	SharpObject? GetParent(string id);

	IEnumerable<SharpObject> GetParents(string id);

	Task<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref);

	Task<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="value">The value to place into the attribute</param>
	/// <returns>Success or Failure</returns>
	Task<bool> SetAttributeAsync(DBRef dbref, string[] attribute, string value, SharpPlayer owner);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	Task<bool> SetAttributeFlagAsync(DBRef dbref, string[] attribute, SharpAttributeFlag flag);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <returns>Success or Failure</returns>
	Task<bool> UnsetAttributeFlagAsync(DBRef dbref, string[] attribute, SharpAttributeFlag flag);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="flagName">Flag</param>
	/// <returns>Success or Failure</returns>
	Task<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <returns>Success or Failure</returns>
	Task<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync();

	/// <summary>
	/// Sets an attribute to string.Empty, or if it has no children, removes it entirely.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <returns>Success or Failure</returns>
	Task<bool> ClearAttributeAsync(DBRef dbref, string[] attribute);

	/// <summary>
	/// Wipe an attribute and all of its children.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <returns>Success or Failure</returns>
	Task<bool> WipeAttributeAsync(DBRef dbref, string[] attribute);

	Task<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj);
	
	Task<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1);
	
	Task<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1);

	Task<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj);

	Task<IEnumerable<AnySharpContent>?> GetContentsAsync(AnyOptionalSharpObject node);

	Task<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj);

	Task<IEnumerable<SharpExit>?> GetExitsAsync(AnyOptionalSharpContainer node);

	Task MoveObject(AnySharpContent enactorObj, DBRef destination);
}