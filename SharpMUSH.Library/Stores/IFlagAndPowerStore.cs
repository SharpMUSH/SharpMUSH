using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Flag and power definitions, and their assignment to objects.
/// </summary>
public interface IFlagAndPowerStore
{
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
	/// <param name="symbol">The power's one-character abbreviation, or the empty string for none</param>
	/// <param name="system">Whether this is a system power</param>
	/// <param name="setPermissions">Permissions required to set this power</param>
	/// <param name="unsetPermissions">Permissions required to unset this power</param>
	/// <param name="typeRestrictions">Object types this power can be set on</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The created power, or null if creation failed</returns>
	ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, string symbol, bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default);

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
	/// <param name="symbol">The power's one-character abbreviation, or the empty string for none</param>
	/// <param name="setPermissions">Permissions required to set this power</param>
	/// <param name="unsetPermissions">Permissions required to unset this power</param>
	/// <param name="typeRestrictions">Object types this power can be set on</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UpdatePowerAsync(string name, string alias, string symbol, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default);

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
	/// Unset an Object flag.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default);

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
}
