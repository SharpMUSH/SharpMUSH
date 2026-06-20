using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Phase 4 of the plugin system: deposits and removes the compiled C# plugin
/// DLL(s) a <see cref="PackageKind.Managed"/> package carries.
///
/// <para><b>Trust model.</b> A managed package distributes arbitrary compiled
/// code that, once loaded, runs in <b>full server trust</b> — there is no
/// sandbox, exactly as for a plugin dropped into <c>plugins/</c> by hand (mirrors
/// docs/design/custom-widgets.md). So an install is gated twice: the operator's
/// explicit per-apply <see cref="PackageApplyRequest.AllowManagedCode"/> opt-in,
/// <i>and</i> a server-side allow-list of package ids the operator pre-approved.
/// Both must pass before a single byte is written. SHA-256 verification on top
/// guards integrity (the bytes are what the manifest signed off on), not trust.</para>
///
/// <para>A freshly-installed plugin is loaded on the <b>next boot</b> — the plugin
/// loader runs at startup. Live hot-load of a newly-installed package is a
/// possible future nicety, not implemented here.</para>
/// </summary>
public interface IManagedPackageInstaller
{
	/// <summary>
	/// Verifies, trust-gates, and deposits a managed package's binaries into
	/// <c>plugins/&lt;packageId&gt;/</c>. Returns the deposited file names (to record
	/// on the registry record) on success; an <see cref="Error{T}"/> — having
	/// written nothing — when the trust gate is not satisfied, the server version
	/// is too old, a carried file is missing, or a SHA-256 does not match.
	/// </summary>
	Task<OneOf<IReadOnlyList<string>, Error<string>>> DeployAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		IManagedPackageBinarySource binarySource,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a managed package's deposited directory (<c>plugins/&lt;packageId&gt;/</c>)
	/// and, when the plugin is currently loaded and unloadable, unloads it from the
	/// live engine. Idempotent — a directory that is already gone is a no-op.
	/// </summary>
	Task<OneOf<Success, Error<string>>> RemoveAsync(
		string packageId,
		IReadOnlyList<string> deployedFiles,
		CancellationToken cancellationToken = default);
}
