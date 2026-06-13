using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Storage for the softcode package registry: installed packages, the objects
/// and attributes they manage (with full baseline values), dependency edges,
/// configured remotes, and per-apply revisions (decisions 20.3, 20.13).
/// Implemented by every database provider; system data, never visible to
/// softcode, travels with backups.
/// </summary>
/// <remarks>
/// Single-fetch methods return <c>OneOf&lt;T, NotFound&gt;</c> rather than null,
/// matching <see cref="IWikiService"/> conventions.
/// </remarks>
public interface IPackageRegistryService
{
	// ── Installed packages ───────────────────────────────────────────────────

	/// <summary>Creates or replaces the registry record for an installed package (keyed by <see cref="InstalledPackageRecord.Id"/>).</summary>
	Task UpsertInstalledPackageAsync(InstalledPackageRecord package);

	/// <summary>Fetches one installed package by id.</summary>
	Task<OneOf<InstalledPackageRecord, NotFound>> GetInstalledPackageAsync(string packageId);

	/// <summary>Lists all installed packages, ordered by id.</summary>
	Task<IReadOnlyList<InstalledPackageRecord>> GetInstalledPackagesAsync();

	/// <summary>
	/// Removes an installed package and ALL its registry data: created-object
	/// records, managed attributes, dependency edges (both directions), and
	/// revisions. Does not touch game objects — the uninstall engine does that.
	/// </summary>
	Task RemoveInstalledPackageAsync(string packageId);

	// ── Package-created objects ──────────────────────────────────────────────

	/// <summary>Records that a package created an object (idempotent on package+ref).</summary>
	Task UpsertPackageObjectAsync(PackageObjectRecord record);

	/// <summary>Lists the objects a package created, ordered by ref.</summary>
	Task<IReadOnlyList<PackageObjectRecord>> GetPackageObjectsAsync(string packageId);

	/// <summary>Removes one created-object record (e.g. after the object is destroyed by an upgrade delete).</summary>
	Task RemovePackageObjectAsync(string packageId, string @ref);

	// ── Managed attributes (full baselines, decision 20.13) ─────────────────

	/// <summary>Creates or replaces a managed-attribute baseline (keyed by package+objid+attribute).</summary>
	Task UpsertManagedAttributeAsync(ManagedAttributeRecord record);

	/// <summary>Lists every attribute a package manages, across all objects.</summary>
	Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesAsync(string packageId);

	/// <summary>Lists every package-managed attribute on one object (cross-package attrs included).</summary>
	Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesForObjectAsync(string objid);

	/// <summary>Removes one managed-attribute record.</summary>
	Task RemoveManagedAttributeAsync(string packageId, string objid, string attribute);

	// ── Managed object structure (flags/powers/locks/attribute flags) ────────

	/// <summary>Creates or replaces a managed object-structure baseline (keyed by package+objid).</summary>
	Task UpsertManagedStructureAsync(ManagedStructureRecord record);

	/// <summary>Lists every object-structure baseline a package manages, across all objects.</summary>
	Task<IReadOnlyList<ManagedStructureRecord>> GetManagedStructuresAsync(string packageId);

	/// <summary>Removes one managed object-structure baseline.</summary>
	Task RemoveManagedStructureAsync(string packageId, string objid);

	// ── Dependencies ─────────────────────────────────────────────────────────

	/// <summary>
	/// Replaces the dependency edges originating from a package (called per
	/// install/upgrade with the manifest's resolved dependency set).
	/// </summary>
	Task SetPackageDependenciesAsync(string packageId, IReadOnlyList<PackageDependencyRecord> dependencies);

	/// <summary>Lists what a package depends on.</summary>
	Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependenciesAsync(string packageId);

	/// <summary>Lists what depends on a package (uninstall blocking, decision 20.6).</summary>
	Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependentsAsync(string packageId);

	// ── Remotes ──────────────────────────────────────────────────────────────

	/// <summary>Creates or replaces a configured remote (keyed by name).</summary>
	Task UpsertPackageRemoteAsync(PackageRemoteRecord remote);

	/// <summary>Lists configured remotes, ordered by name.</summary>
	Task<IReadOnlyList<PackageRemoteRecord>> GetPackageRemotesAsync();

	/// <summary>Fetches one remote by name.</summary>
	Task<OneOf<PackageRemoteRecord, NotFound>> GetPackageRemoteAsync(string name);

	/// <summary>Removes a configured remote.</summary>
	Task RemovePackageRemoteAsync(string name);

	// ── Revisions (decision 20.13) ───────────────────────────────────────────

	/// <summary>Appends a revision record (caller assigns the next monotonic number).</summary>
	Task AddPackageRevisionAsync(PackageRevisionRecord revision);

	/// <summary>Lists a package's revisions, newest first.</summary>
	Task<IReadOnlyList<PackageRevisionRecord>> GetPackageRevisionsAsync(string packageId);

	/// <summary>Fetches one revision.</summary>
	Task<OneOf<PackageRevisionRecord, NotFound>> GetPackageRevisionAsync(string packageId, int revision);

	/// <summary>Deletes all but the newest <paramref name="keep"/> revisions of a package.</summary>
	Task PrunePackageRevisionsAsync(string packageId, int keep);
}
