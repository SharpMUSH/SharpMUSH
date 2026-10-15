namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The newest SharpMUSH version a package runs on (<c>maximum_server</c>, format 1.6), written with one to three
/// components. The components left out match anything: <c>2</c> is every 2.x, <c>2.4</c> every 2.4.x, and
/// <c>2.4.1</c> that version alone.
/// </summary>
/// <param name="Version">The written version, its missing components zero.</param>
/// <param name="Components">How many components were written (1 to 3).</param>
public sealed record PackageVersionCeiling(PackageVersion Version, int Components)
{
	/// <summary>Parses one to three numeric components with no prerelease suffix.</summary>
	public static bool TryParse(string? input, out PackageVersionCeiling ceiling)
	{
		ceiling = new PackageVersionCeiling(new PackageVersion(0, 0, 0), 3);
		if (!PackageVersion.TryParse(input, out var version) || version.Prerelease is not null)
		{
			return false;
		}

		ceiling = new PackageVersionCeiling(version, input!.Trim().Split('.').Length);
		return true;
	}

	/// <summary>True when <paramref name="server"/> is newer than every version this ceiling covers.</summary>
	public bool IsExceededBy(PackageVersion server) => Components switch
	{
		1 => server.Major > Version.Major,
		2 => (server.Major, server.Minor).CompareTo((Version.Major, Version.Minor)) > 0,
		_ => (server.Major, server.Minor, server.Patch).CompareTo((Version.Major, Version.Minor, Version.Patch)) > 0
	};

	public override string ToString() => Components switch
	{
		1 => $"{Version.Major}",
		2 => $"{Version.Major}.{Version.Minor}",
		_ => $"{Version.Major}.{Version.Minor}.{Version.Patch}"
	};
}

/// <summary>How a package's engine-version range fits the server it is planned on.</summary>
public enum PackageServerFit
{
	/// <summary>Inside the range, or the package states none.</summary>
	Compatible,

	/// <summary>The server is a newer major version than the newest the author tested (<c>verified_server</c>). A warning.</summary>
	NewerThanVerified,

	/// <summary>The server is newer than <c>maximum_server</c>. A warning the administrator must confirm before applying.</summary>
	AboveMaximum,

	/// <summary>The server does not satisfy <c>requires_server</c>. Blocks the plan.</summary>
	BelowMinimum
}

/// <summary>
/// Where the server sits in a package's engine-version range (<c>requires_server</c>, <c>verified_server</c>,
/// <c>maximum_server</c>), for the review screen and the apply gate.
/// </summary>
/// <param name="Fit">The outcome; the most serious one applies when several do.</param>
/// <param name="Server">This server's version.</param>
/// <param name="Minimum">The manifest's <c>requires_server</c>, or null.</param>
/// <param name="Verified">The manifest's <c>verified_server</c>, or null.</param>
/// <param name="Maximum">The manifest's <c>maximum_server</c>, or null.</param>
public sealed record PackageServerCompatibility(
	PackageServerFit Fit,
	string Server,
	string? Minimum,
	string? Verified,
	string? Maximum)
{
	/// <summary>Places <paramref name="server"/> in the range <paramref name="manifest"/> states.</summary>
	public static PackageServerCompatibility Check(PackageManifest manifest, PackageVersion server)
	{
		var fit = manifest switch
		{
			{ RequiresServer: { } minimum } when !minimum.IsSatisfiedBy(server, includePrereleases: true) => PackageServerFit.BelowMinimum,
			{ MaximumServer: { } maximum } when maximum.IsExceededBy(server) => PackageServerFit.AboveMaximum,
			{ VerifiedServer: { } verified } when server.Major > verified.Major => PackageServerFit.NewerThanVerified,
			_ => PackageServerFit.Compatible
		};

		return new PackageServerCompatibility(fit, server.ToString(), manifest.RequiresServer?.ToString(),
			manifest.VerifiedServer?.ToString(), manifest.MaximumServer?.ToString());
	}
}

/// <summary>
/// An administrator's confirmation to apply a package on a server newer than its <c>maximum_server</c>, kept on
/// <see cref="InstalledPackageRecord.PastMaximum"/> until an apply inside the range clears it.
/// </summary>
/// <param name="Server">The server version the package was applied on.</param>
/// <param name="Maximum">The manifest's <c>maximum_server</c> at the time.</param>
/// <param name="At">When the administrator confirmed it.</param>
public sealed record PackageMaximumOverride(string Server, string Maximum, DateTimeOffset At);

/// <summary>
/// The SharpMUSH version this server runs, which package manifests' <c>requires_server</c>, <c>verified_server</c> and
/// <c>maximum_server</c> are checked against. The host registers it from the version <c>version()</c> reports.
/// </summary>
/// <param name="Current">The server's version.</param>
public sealed record EngineVersion(PackageVersion Current)
{
	/// <summary>Reads an assembly version (<c>2.0.0.0</c>): its first three components, the fourth dropped.</summary>
	public static EngineVersion FromAssemblyVersion(string assemblyVersion)
	{
		var version = Version.Parse(assemblyVersion);
		return new EngineVersion(new PackageVersion(version.Major, version.Minor, Math.Max(version.Build, 0)));
	}
}
