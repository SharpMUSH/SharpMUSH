using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IFlagAndPowerStore"/>: flag and power definitions, and their assignment to objects. Not
/// ported yet — every member throws <see cref="NotImplementedException"/> until Task 8. (The definitions
/// themselves are seeded by <c>Migrate()</c> in <c>Migration/LightningMigration.cs</c>; reading them back
/// through this interface is what remains.)
/// </summary>
public sealed partial class LightningDatabase
{
	public ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol, bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, string symbol, bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UpdatePowerAsync(string name, string alias, string symbol, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
