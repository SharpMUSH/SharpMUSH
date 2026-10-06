using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Sets, keeps and gives back the configuration options a package declares in <c>settings:</c>
/// (<see cref="PackageSettingSpec"/>).
/// <list type="bullet">
/// <item>Installing sets each option, checked first against the option as <c>@config/set</c> would check it, and
/// records the value it replaced (<see cref="InstalledPackageRecord.Settings"/>). An option another installed
/// package set blocks the plan.</item>
/// <item>An upgrade sets an option again only when the new version changes its value; otherwise the game's value
/// stands, so an administrator's change survives.</item>
/// <item>An option the new version drops, and every option on uninstall, gets back the value it replaced, unless
/// the game has changed it since the package set it: then it is left as it is.</item>
/// </list>
/// Writes are tracked by the operation's <see cref="PackageWriteTransaction"/>, so a failed apply undoes them.
/// </summary>
public interface IPackageSettingService
{
	/// <summary>
	/// What applying <paramref name="declared"/> for <paramref name="packageId"/> would do, read-only.
	/// <paramref name="resolve"/> turns an object ref into its objid, or null when the object does not exist yet.
	/// </summary>
	Task<IReadOnlyList<PackageSettingChange>> PlanAsync(
		string packageId,
		IReadOnlyList<PackageSettingSpec> declared,
		IReadOnlyList<PackageSettingRecord>? owned,
		Func<PackageRef, string?> resolve);

	/// <summary>
	/// Applies <paramref name="declared"/> (empty to let go of everything the package set). Returns what the
	/// package owns afterwards, or why it cannot be applied.
	/// </summary>
	Task<Result<IReadOnlyList<PackageSettingRecord>>> ApplyAsync(
		PackageWriteTransaction writes,
		string packageId,
		IReadOnlyList<PackageSettingSpec> declared,
		IReadOnlyList<PackageSettingRecord>? owned,
		Func<PackageRef, string?> resolve,
		List<string> notes);
}
