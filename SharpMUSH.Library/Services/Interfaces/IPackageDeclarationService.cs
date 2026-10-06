using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Installs, upgrades and removes what a package declares beyond its objects: role and permission
/// categories, custom permissions and roles (<see cref="PackageDeclarations"/>).
/// <list type="bullet">
/// <item>An item that does not exist is created and becomes the package's (recorded in
/// <see cref="InstalledPackageRecord.Owned"/>). One the game already has is used as it is: the package
/// never changes or removes it. One that belongs to another package blocks the plan.</item>
/// <item>On upgrade each of the package's items is merged field by field against what the previous
/// version declared: a field the new version changes takes the new value, and any other field keeps
/// what the game has, so an administrator's edits survive. A role's permissions merge per permission.</item>
/// <item>An item the new version drops, and every item on uninstall, is removed, except a role someone
/// holds, a permission a remaining role sets, and a category something remaining is in: those are kept
/// and stop being the package's.</item>
/// </list>
/// Writes go through the operation's <see cref="PackageWriteTransaction"/>, so a failed apply undoes them.
/// </summary>
public interface IPackageDeclarationService
{
	/// <summary>What applying <paramref name="declared"/> for <paramref name="packageId"/> would do, read-only.</summary>
	Task<IReadOnlyList<PackageDeclarationChange>> PlanAsync(
		string packageId, PackageDeclarations declared, PackageDeclarations? owned, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies <paramref name="declared"/> (<see cref="PackageDeclarations.None"/> to retire everything the
	/// package owns). Returns what the package owns afterwards, or why it cannot be applied.
	/// </summary>
	Task<Result<PackageDeclarations>> ApplyAsync(
		PackageWriteTransaction writes,
		string packageId,
		PackageDeclarations declared,
		PackageDeclarations? owned,
		List<string> notes,
		CancellationToken cancellationToken = default);
}
