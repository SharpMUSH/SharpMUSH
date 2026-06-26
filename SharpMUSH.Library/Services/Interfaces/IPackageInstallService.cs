using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The orchestration layer of the package plan/apply model: gathers live game
/// state and registry records for the plan engine, executes reviewed
/// changesets against the live database (objects owned by the Package Manager
/// wizard, decision 20.2), records baselines and revisions (20.13), and
/// drives uninstall and rollback.
/// </summary>
public interface IPackageInstallService
{
	/// <summary>
	/// Computes the changeset an apply would execute: gathers the live-state
	/// snapshot and registry records, then runs the pure plan engine.
	/// Read-only. Re-run after configure answers change.
	/// </summary>
	Task<PackageChangeset> PlanAsync(
		PackageManifest manifest,
		IReadOnlyDictionary<string, string>? configureAnswers = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a manifest: re-plans, validates every conflict has a decision,
	/// creates/updates/deletes objects and attributes, and records registry
	/// state plus a revision snapshot. Fails without writing when the plan is
	/// blocked or a conflict is undecided.
	///
	/// <para>For a <see cref="PackageKind.Managed"/> package (Phase 4 — a compiled
	/// C# plugin DLL), this instead verifies and deposits the carried binaries via
	/// <paramref name="binarySource"/> into <c>plugins/&lt;id&gt;/</c> (subject to the
	/// trust gate on <paramref name="request"/>) and records the deployed file
	/// list; the plugin loads on the next boot. A managed apply requires a
	/// <paramref name="binarySource"/>.</para>
	/// </summary>
	Task<OneOf<PackageApplyResult, Error<string>>> ApplyAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		CancellationToken cancellationToken = default,
		IManagedPackageBinarySource? binarySource = null);

	/// <summary>
	/// Uninstalls a package: blocks when dependents exist (unless
	/// <paramref name="force"/>), marks created objects GOING (the @destroy
	/// convention), clears managed attributes on shared objects, and removes
	/// all registry records.
	/// </summary>
	Task<OneOf<Success, Error<string>>> UninstallAsync(
		string packageId,
		bool force = false,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Rolls back to a prior revision's snapshot, applied as a NEW revision
	/// (decision 20.13). Restores managed attribute values on objects that
	/// still exist; anything unrestorable is reported in the result notes.
	/// </summary>
	Task<OneOf<PackageRollbackResult, Error<string>>> RollbackAsync(
		string packageId,
		int revision,
		CancellationToken cancellationToken = default);
}
