﻿using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database
{
	public interface ISharpDatabase
	{
		Task Migrate();

		Task<int> CreatePlayerAsync(string name, string password, DBRef location);

		Task<int> CreateRoomAsync(string name, SharpPlayer creator);

		Task<int> CreateThingAsync(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator);

		Task<int> CreateExitAsync(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator);

		Task<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, string[] attribute);
		
		Task<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern);

		/// <summary>
		/// Get the Object represented by a Database Reference Number.
		/// Optionally passing either the CreatedSecs or CreatedMilliseconds will do a more specific lookup.
		/// </summary>
		/// <param name="dbref">Database Reference Number</param>
		/// <param name="createdmsecs">Created Milliseconds (Unix Timestamp</param>
		/// <returns>A OneOf over the object being returned</returns>
		Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> GetObjectNodeAsync(DBRef dbref);

		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> GetObjectNode(DBRef dbref);

		Task<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref);

		Task<SharpPlayer?> GetPlayerByNameAsync(string name);

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

		Task<IEnumerable<OneOf<SharpPlayer, SharpExit, SharpThing>>> GetNearbyObjectsAsync(DBRef obj);

		Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> GetLocationAsync(DBRef obj, int depth = 1);

		Task<IEnumerable<OneOf<SharpPlayer, SharpExit, SharpThing, None>>?> GetContentsAsync(DBRef obj);

		Task<IEnumerable<OneOf<SharpPlayer, SharpExit, SharpThing, None>>?> GetContentsAsync(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> node);

	}
}