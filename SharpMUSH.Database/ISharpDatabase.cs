using OneOf;
using SharpMUSH.Database.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database
{
	public interface ISharpDatabase
	{
		Task Migrate();

		Task<int> CreatePlayer(string name, string passwordHash, OneOf<SharpPlayer, SharpRoom, SharpThing> location);

		Task<int> CreateRoom(string name, SharpPlayer creator);

		Task<int> CreateThing(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator);

		Task<int> CreateExit(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator);

		Task<SharpAttribute[]?> GetAttribute(DBRef dbref, string[] attribute);
		
		Task<SharpAttribute[]?> GetAttributes(DBRef dbref, string attribute_pattern);

		/// <summary>
		/// Get the Object represented by a Database Reference Number.
		/// Optionally passing either the createdsecs or createdmiliseconds will do a more specific lookup.
		/// </summary>
		/// <param name="dbref">Database Reference Number</param>
		/// <param name="createdmsecs">Created Miliseconds (Unix Timestamp</param>
		/// <returns>A OneOf over the object being returned</returns>
		Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>?> GetObjectNode(DBRef dbref, int? createdmsecs = null);

		/// <summary>
		/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
		/// </summary>
		/// <param name="dbref">Database Reference Number</param>
		/// <param name="attribute">Attribute Path.</param>
		/// <param name="value">The value to place into the attribute</param>
		/// <returns>Success or Failure</returns>
		Task<bool> SetAttribute(DBRef dbref, string[] attribute, string value, SharpPlayer owner);

		/// <summary>
		/// Sets an attribute to string.Empty, or if it has no children, removes it entirely.
		/// This does not do any checks regarding permissions, as that is up to the functionality itself.
		/// </summary>
		/// <param name="dbref">Database Reference Number</param>
		/// <param name="attribute">Attribute Path.</param>
		/// <returns>Success or Failure</returns>
		Task<bool> ClearAttribute(DBRef dbref, string[] attribute);
	}
}