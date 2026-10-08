using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Deposits and removes the compiled C# plugin a <see cref="PackageKind.Plugin"/> package carries.
///
/// <para><b>Trust model.</b> A plugin runs in <b>full server trust</b>; there is no sandbox, exactly as for a plugin
/// dropped into the plugins directory by hand. An install needs the host to allow plugin packages
/// (<see cref="PluginInstallOptions"/>, on by default) and the administrator's per-apply
/// <see cref="PackageApplyRequest.AllowPluginCode"/> confirmation. SHA-256 verification guards integrity (the bytes
/// are what the manifest committed to), not trust.</para>
///
/// <para>A freshly installed plugin loads at the <b>next start</b>.</para>
/// </summary>
public interface IPluginPackageInstaller
{
	/// <summary>
	/// Verifies, trust-gates, and deposits a plugin package's binaries into
	/// <c>&lt;installed plugins&gt;/&lt;packageId&gt;/</c>. Returns the deposited file names (to record
	/// on the registry record) on success; an <see cref="Error{T}"/> — having
	/// written nothing — when the host does not allow plugin packages, the administrator did not confirm, its plugin.json does not match the package, the server version
	/// is too old, a carried file is missing, or a SHA-256 does not match.
	/// </summary>
	Task<Result<IReadOnlyList<string>>> DeployAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		IPluginPackageBinarySource binarySource,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a plugin package's deposited directory (<c>&lt;installed plugins&gt;/&lt;packageId&gt;/</c>)
	/// and, when the plugin is currently loaded and unloadable, unloads it from the
	/// live engine. Idempotent — a directory that is already gone is a no-op.
	/// </summary>
	Task<Result<Success>> RemoveAsync(
		string packageId,
		IReadOnlyList<string> deployedFiles,
		CancellationToken cancellationToken = default);
}
