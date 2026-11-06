namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The official remote library, SharpMUSH.Packages: where the community directory is read from when no official
/// remote is configured, and where a package that leaves the bundled catalogue goes on to be maintained.
/// </summary>
public static class OfficialPackageRemote
{
	/// <summary>The library's repository.</summary>
	public const string Url = "https://github.com/SharpMUSH/SharpMUSH.Packages";

	/// <summary>The name the library is listed under when no official remote is configured.</summary>
	public const string Name = "SharpMUSH Official";
}
