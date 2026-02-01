using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
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
	/// <param name="password">Player password (plaintext for new players, or pre-hashed for imports)</param>
	/// <param name="location">Location to create it in</param>
	/// <param name="home"></param>
	/// <param name="quota">Initial quota for the player</param>
	/// <param name="salt">Optional salt for imported passwords (null for new players)</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New player <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
		string? salt = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a hashed password for a player.
	/// </summary>
	/// <param name="player">Player</param>
	/// <param name="password">plaintext password</param>
	/// <param name="salt">Optional salt for imported passwords (null for new passwords)</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the quota for a player.
	/// </summary>
	/// <param name="player">Player</param>
	/// <param name="quota">New quota amount</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the count of objects owned by a player.
	/// </summary>
	/// <param name="player">Player whose objects to count</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Number of objects owned by the player</returns>
	ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new room.
	/// </summary>
	/// <param name="name">Room Name</param>
	/// <param name="creator">Room Player-Creator</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New room <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new thing.
	/// </summary>
	/// <param name="name">Thing name</param>
	/// <param name="location">Location to create it in</param>
	/// <param name="creator">Owner to the thing</param>
	/// <param name="home">Home location for the thing</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator, AnySharpContainer home, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new exit.
	/// </summary>
	/// <param name="name">Exit name</param>
	/// <param name="aliases">Exit Aliases</param>
	/// <param name="location">Location for the Exit</param>
	/// <param name="creator">Owner to the exit</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>New thing <see cref="DBRef"/></returns>
	ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator, CancellationToken cancellationToken = default);

	/// <summary>
	/// Link an exit to a destination location.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <param name="location"><see cref="AnySharpContainer"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if all elements existed and were able to be linked</returns>
	ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unlink an exit from its destination location. Does not clear the DESTINATION / EXITTO attribute.
	/// </summary>
	/// <param name="exit"><see cref="SharpExit"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if the element still existed and could be unlinked.</returns>
	ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default);

	/// <summary>
	/// Link a room to a location (drop-to).
	/// </summary>
	/// <param name="room"><see cref="SharpRoom"/></param>
	/// <param name="location"><see cref="AnyOptionalSharpContainer"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if all elements existed and were able to be linked</returns>
	ValueTask<bool> LinkRoomAsync(SharpRoom room, AnyOptionalSharpContainer location, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unlink a room from its location (drop-to).
	/// </summary>
	/// <param name="room"><see cref="SharpRoom"/></param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success if the element still existed and could be unlinked.</returns>
	ValueTask<bool> UnlinkRoomAsync(SharpRoom room, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	/// <param name="lockData">The lock data including string and flags</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetLockAsync(SharpObject target, string lockName, Models.SharpLockData lockData, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Unset the lock of an object.
	/// </summary>
	/// <param name="target">What object to lock</param>
	/// <param name="lockName">The name of the lock</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get the attribute value of an object's attribute.
	/// </summary>
	/// <param name="dbref">DBRef of an object to get the attributes for</param>
	/// <param name="attribute">Attribute Path - uses attribute leaves</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The <see cref="SharpAttribute"/> hierarchy, with the last attribute being the final leaf.</returns>
	IAsyncEnumerable<SharpAttribute>? GetAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	// TODO: Return type for attribute pattern queries needs reconsideration.
	// Attribute patterns return multiple attribute paths, so return type should ideally be
	// IEnumerable<IEnumerable<SharpAttribute>> to represent full paths for each match.
	IAsyncEnumerable<SharpAttribute>? GetAttributesAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);
	
	IAsyncEnumerable<SharpAttribute>? GetAttributesByRegexAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);

	IAsyncEnumerable<LazySharpAttribute>? GetLazyAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);
	
	IAsyncEnumerable<LazySharpAttribute>? GetLazyAttributesAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);
	
	IAsyncEnumerable<LazySharpAttribute>? GetLazyAttributesByRegexAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Get an attribute with full inheritance chain resolution in a single database call.
	/// Follows the inheritance order: object → parent chain (parent, grandparent, etc.) → object's zones → parent's zones → grandparent's zones, etc.
	/// This means PARENTS TAKE PRECEDENCE OVER ZONES at all levels.
	/// Returns the complete attribute path (FOO → BAR → BAZ) from the first object in the inheritance chain where the attribute is found.
	/// Streams each attribute in the path with inherited flags merged from deeper inheritance levels.
	/// </summary>
	/// <param name="dbref">DBRef of the object to start the search from</param>
	/// <param name="attribute">Attribute path to search for (e.g., ["FOO", "BAR", "BAZ"])</param>
	/// <param name="checkParent">Whether to check parent and zone inheritance chains</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Stream of AttributeWithInheritance for each segment in the attribute path, or empty if not found</returns>
	IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lazy version of GetAttributeWithInheritanceAsync for efficient retrieval.
	/// Returns the complete attribute path (FOO → BAR → BAZ) from the first object in the inheritance chain where the attribute is found.
	/// </summary>
	IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Get all attribute entries from the attribute table.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Async enumerable of all attribute entries</returns>
	IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Get a specific attribute entry by name.
	/// </summary>
	/// <param name="name">Attribute name</param>
	/// <param name="ct">Cancellation Token</param>
	/// <returns>The attribute entry if found, null otherwise</returns>
	ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default);

	/// <summary>
	/// Create or update an attribute entry in the attribute table.
	/// </summary>
	/// <param name="name">Attribute name</param>
	/// <param name="defaultFlags">Default flags for this attribute</param>
	/// <param name="limit">Optional regex pattern to limit values</param>
	/// <param name="enumValues">Optional enumeration of allowed values</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The created or updated attribute entry</returns>
	ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags, string? limit = null, string[]? enumValues = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete an attribute entry from the attribute table.
	/// </summary>
	/// <param name="name">Attribute name</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Get the Object represented by a Database Reference Number.
	/// Optionally passing either the CreatedSecs or CreatedMilliseconds will do a more specific lookup.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A OneOf over the object being returned</returns>
	ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get an Object Flag by name, if it exists.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A SharpObjectFlag, or null if it does not exist</returns>
	ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all known Object Flags
	/// </summary>
	/// <returns>A list of all SharpObjectFlags</returns>
	IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new Object Flag.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <param name="aliases">Flag aliases</param>
	/// <param name="symbol">Flag symbol</param>
	/// <param name="system">Whether this is a system flag</param>
	/// <param name="setPermissions">Permissions required to set this flag</param>
	/// <param name="unsetPermissions">Permissions required to unset this flag</param>
	/// <param name="typeRestrictions">Object types this flag can be set on</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The created flag, or null if creation failed</returns>
	ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol, bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete an Object Flag by name.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an Object Flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Set an Object Power.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="power">Power</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Unset an Object Power.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="power">Power</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default);

	/// <summary>
	/// Create a new Power.
	/// </summary>
	/// <param name="name">Power name</param>
	/// <param name="alias">Power alias</param>
	/// <param name="system">Whether this is a system power</param>
	/// <param name="setPermissions">Permissions required to set this power</param>
	/// <param name="unsetPermissions">Permissions required to unset this power</param>
	/// <param name="typeRestrictions">Object types this power can be set on</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The created power, or null if creation failed</returns>
	ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete a Power by name.
	/// </summary>
	/// <param name="name">Power name</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Update an existing Power.
	/// </summary>
	/// <param name="name">Power name</param>
	/// <param name="alias">Power alias</param>
	/// <param name="setPermissions">Permissions required to set this power</param>
	/// <param name="unsetPermissions">Permissions required to unset this power</param>
	/// <param name="typeRestrictions">Object types this power can be set on</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UpdatePowerAsync(string name, string alias, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default);

	/// <summary>
	/// Update an existing object flag.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <param name="aliases">Flag aliases</param>
	/// <param name="symbol">Flag symbol</param>
	/// <param name="setPermissions">Permissions required to set this flag</param>
	/// <param name="unsetPermissions">Permissions required to unset this flag</param>
	/// <param name="typeRestrictions">Object types this flag can be set on</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set the disabled state of an object flag.
	/// System flags cannot be disabled.
	/// </summary>
	/// <param name="name">Flag name</param>
	/// <param name="disabled">True to disable, false to enable</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set the disabled state of a power.
	/// System powers cannot be disabled.
	/// </summary>
	/// <param name="name">Power name</param>
	/// <param name="disabled">True to disable, false to enable</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a name on an object.
	/// </summary>
	/// <param name="obj">The object to alter</param>
	/// <param name="value">The value for the field</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Result</returns>
	ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Home of a content object. The 'home' of a Room is its Drop-To.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="home">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Sets the Location of a content object. 
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="location">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="parent">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Sets the Parent of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Zone of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="zone">New Zone</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Unsets the Zone of an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetObjectZone(AnySharpObject obj, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the Owner of an Object to a player.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="owner">New Value</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the warning type flags for an object.
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="warnings">Warning type flags</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetObjectWarnings(AnySharpObject obj, WarningType warnings, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unset an Object flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The representing parent</returns>
	ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all powers the Server knows about.
	/// </summary>
	/// <returns>All powers</returns>
	IAsyncEnumerable<SharpPower> GetObjectPowersAsync(CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Get a Power by name, if it exists.
	/// </summary>
	/// <param name="name">Power name</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A SharpPower, or null if it does not exist</returns>
	ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Get the parent of an object.
	/// </summary>
	/// <param name="id">Child ID</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The full representing parent chain</returns>
	IAsyncEnumerable<SharpObject> GetParentsAsync(string id, CancellationToken cancellationToken = default);

	ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all objects in the database as a streaming AsyncEnumerable.
	/// This allows for efficient filtering and searching without loading all objects into memory.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of all SharpObjects in the database</returns>
	IAsyncEnumerable<SharpObject> GetAllObjectsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Get objects from the database with filtering applied at the database level.
	/// This is more efficient than loading all objects and filtering in application code.
	/// Lock evaluation must happen in application code, but other filters can be pushed to the database.
	/// </summary>
	/// <param name="filter">Filter criteria to apply at database level</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of filtered SharpObjects</returns>
	IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets all players in the database as a streaming AsyncEnumerable.
	/// This allows for efficient processing of all players without loading them all into memory.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of all SharpPlayers in the database</returns>
	IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all exits that lead to a specific destination.
	/// </summary>
	/// <param name="destination">The destination DBRef</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of exits leading to the destination</returns>
	IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="value">The value to place into the attribute</param>
	/// <param name="owner">Attribute Owner</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="attr">Attribute</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="attr"></param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="flagName">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <returns>Success or Failure</returns>
	IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets an attribute to string.Empty, or if it has no children, removes it entirely.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	/// <summary>
	/// Wipe an attribute and all of its children.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(DBRef obj, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(AnySharpObject obj, CancellationToken cancellationToken = default);

	ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default);

	ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, CancellationToken cancellationToken = default);

	IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node,
		CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpExit>? GetExitsAsync(DBRef obj, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, CancellationToken cancellationToken = default);

	ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="id">Location ID</param>
	/// <param name="depth">Depth</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The deepest findable object based on depth</returns>
	ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(string id, string type,
		CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, CancellationToken cancellationToken = default);

	ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, CancellationToken cancellationToken = default);
	
	IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, CancellationToken cancellationToken = default);

	ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all objects that belong to a specific zone.
	/// </summary>
	/// <param name="zone">The zone object</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>An async enumerable of all objects in the zone</returns>
	IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, CancellationToken cancellationToken = default);

	ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default);
	
	ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default);
	
	ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default);
	
	ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default);
	
	ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default);
	
	ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Gets ALL mail in the system regardless of owner or folder.
	/// WARNING: This bypasses all access controls and should only be used in God-level administrative operations.
	/// </summary>
	IAsyncEnumerable<SharpMail> GetAllSystemMailAsync(CancellationToken cancellationToken = default);
		
	/// <summary>
	/// Sets expanded data for a SharpObject, that does not fit on the light-weight nature of a SharpObject or Attributes.
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the Expanded Object Data for a SharpObject. 
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default);


	/// <summary>
	/// Sets expanded data for a SharpObject, for the server as a whole.
	/// </summary>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the Expanded Object Data for the server as a whole. 
	/// </summary>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpChannel> GetAllChannelsAsync(CancellationToken cancellationToken = default);
	
	ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default);
	
	IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, CancellationToken cancellationToken = default);

	ValueTask CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default);
	
	ValueTask UpdateChannelAsync(SharpChannel channel,
		MString? name,
		MString? description,
		string[]? privs,
		string? joinLock,
		string? speakLock,
		string? seeLock,
		string? hideLock,
		string? modLock,
		string? mogrifier,
		int? buffer, CancellationToken cancellationToken = default);
	
	ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default);
	
	ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default);

	ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default);
	
	ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default);
	
	ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default);
	
	/// <summary>
	/// Checks if there is a path from startObject to targetObject following parent and/or zone edges.
	/// Uses graph traversal to detect potential cycles in combined parent/zone chains.
	/// </summary>
	/// <param name="startObject">Starting object for traversal</param>
	/// <param name="targetObject">Target object to find</param>
	/// <param name="maxDepth">Maximum depth for traversal (default 100)</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if a path exists from start to target, false otherwise</returns>
	ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject, int maxDepth = 100, CancellationToken cancellationToken = default);
}
