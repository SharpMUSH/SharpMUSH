using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library
{
	public interface ISharpDatabase
	{
		Task Migrate();

		Task<DBRef> CreatePlayerAsync(string name, string password, DBRef location);

		Task<DBRef> CreateRoomAsync(string name, SharpPlayer creator);

		Task<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator);

		Task<DBRef> CreateExitAsync(string name, AnySharpContainer location, SharpPlayer creator);

		Task SetLockAsync(SharpObject target, string lockName, string lockString);

		Task<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, string[] attribute);
		
		Task<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern);

		/// <summary>
		/// Get the Object represented by a Database Reference Number.
		/// Optionally passing either the CreatedSecs or CreatedMilliseconds will do a more specific lookup.
		/// </summary>
		/// <param name="dbref">Database Reference Number</param>
		/// <returns>A OneOf over the object being returned</returns>
		Task<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref);

		AnyOptionalSharpObject GetObjectNode(DBRef dbref);

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

		Task<IEnumerable<AnySharpContent>> GetNearbyObjectsAsync(DBRef obj);

		Task<AnyOptionalSharpObject> GetLocationAsync(DBRef obj, int depth = 1);

		Task<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj);

		Task<IEnumerable<AnySharpContent>?> GetContentsAsync(AnyOptionalSharpObject node);
	}
}