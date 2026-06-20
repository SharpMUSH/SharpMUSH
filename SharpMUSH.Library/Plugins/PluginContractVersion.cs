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
	public static readonly PackageVersion Current = new(1, 0, 0);

	/// <summary>True when this server satisfies a managed package's minimum requirement.</summary>
	public static bool Satisfies(VersionConstraint minServerVersion) =>
		minServerVersion.IsSatisfiedBy(Current, includePrereleases: true);
}
