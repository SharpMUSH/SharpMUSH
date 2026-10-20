using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// The plugin/server contract version a managed package's
/// <c>binaries.min_server_version</c> is checked against (Phase 4). It tracks the
/// shared contract surface in <c>SharpMUSH.Library</c> that plugin DLLs bind to,
/// so a package built for a newer contract is refused on an older server rather
/// than failing obscurely at load. Bumped when the plugin contract changes.
/// </summary>
public static class PluginContractVersion
{
	/// <summary>The contract version this server implements.</summary>
	public static readonly PackageVersion Current = new(3, 0, 0);

	/// <summary>
	/// True when this server satisfies a managed package's minimum requirement. The constraint's lower bound
	/// names the contract the plugin was built against, and a major version apart breaks binary compatibility,
	/// so <c>&gt;=2.0</c> is refused by a 3.x server as <c>&gt;=4.0</c> is.
	/// </summary>
	public static bool Satisfies(VersionConstraint minServerVersion) =>
		BuiltAgainst(minServerVersion)?.Major == Current.Major
		&& minServerVersion.IsSatisfiedBy(Current, includePrereleases: true);

	/// <summary>The highest version a clause requires at least, or null when no clause sets a lower bound.</summary>
	private static PackageVersion? BuiltAgainst(VersionConstraint constraint) =>
		constraint.Clauses
			.Where(clause => clause.Comparison is VersionComparison.GreaterOrEqual or VersionComparison.Greater or VersionComparison.Exact)
			.Select(clause => clause.Version)
			.Max();
}
