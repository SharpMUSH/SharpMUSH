namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The identity of the package catalogue embedded in the server assembly, shared by the server
/// (which resolves it) and the portal (which must recognise it: the reserved remote is not a row
/// in <c>sys_remotes</c>, so it cannot be edited or removed). The catalogue's contents live
/// server-side in <c>BundledPackages</c>; only the names travel.
/// </summary>
public static class BundledPackageSource
{
	/// <summary>Reserved remote name for the catalogue. Never stored as a configured remote.</summary>
	public const string RemoteName = "bundled";

	/// <summary>
	/// Source repo recorded against anything installed from the catalogue, whether by first-boot
	/// bootstrap or by an admin from the portal. Not a fetchable URL — it is the marker that says
	/// "this came out of the server image".
	/// </summary>
	public const string SourceRepo = "bundled:sharpmush";

	/// <summary>Commit recorded for a catalogue install. The image has no commits; this is the sentinel.</summary>
	public const string SourceCommit = "bundled";

	/// <summary>True when <paramref name="name"/> is the reserved catalogue remote name.</summary>
	public static bool IsCatalogueRemote(string? name) =>
		string.Equals(name?.Trim(), RemoteName, StringComparison.OrdinalIgnoreCase);

	/// <summary>True when <paramref name="sourceRepo"/> marks a package installed from the catalogue.</summary>
	public static bool IsCatalogueSource(string? sourceRepo) =>
		string.Equals(sourceRepo, SourceRepo, StringComparison.OrdinalIgnoreCase);
}
