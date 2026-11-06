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

	/// <summary>The library's earlier address, which GitHub still redirects; installs made through it record it.</summary>
	public const string FormerUrl = "https://github.com/SharpMUSH/SharpMUSH-Packages";

	/// <summary>True when <paramref name="url"/> names the library, under either address, with or without <c>.git</c> or a trailing <c>/</c>.</summary>
	public static bool IsLibrary(string? url)
	{
		var repo = (url ?? "").Trim().TrimEnd('/');
		if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
		return string.Equals(repo, Url, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(repo, FormerUrl, StringComparison.OrdinalIgnoreCase);
	}
}
