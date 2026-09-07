using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.IPackageRegistryService"/>: the softcode package registry.
/// Not ported yet — every member throws <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public Task UpsertInstalledPackageAsync(InstalledPackageRecord package)
		=> throw new NotImplementedException();

	public Task<OneOf<InstalledPackageRecord, NotFound>> GetInstalledPackageAsync(string packageId)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<InstalledPackageRecord>> GetInstalledPackagesAsync()
		=> throw new NotImplementedException();

	public Task RemoveInstalledPackageAsync(string packageId)
		=> throw new NotImplementedException();

	public Task UpsertPackageObjectAsync(PackageObjectRecord record)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<PackageObjectRecord>> GetPackageObjectsAsync(string packageId)
		=> throw new NotImplementedException();

	public Task RemovePackageObjectAsync(string packageId, string @ref)
		=> throw new NotImplementedException();

	public Task UpsertManagedAttributeAsync(ManagedAttributeRecord record)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesAsync(string packageId)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesForObjectAsync(string objid)
		=> throw new NotImplementedException();

	public Task RemoveManagedAttributeAsync(string packageId, string objid, string attribute)
		=> throw new NotImplementedException();

	public Task UpsertManagedStructureAsync(ManagedStructureRecord record)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<ManagedStructureRecord>> GetManagedStructuresAsync(string packageId)
		=> throw new NotImplementedException();

	public Task RemoveManagedStructureAsync(string packageId, string objid)
		=> throw new NotImplementedException();

	public Task SetPackageDependenciesAsync(string packageId, IReadOnlyList<PackageDependencyRecord> dependencies)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependenciesAsync(string packageId)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependentsAsync(string packageId)
		=> throw new NotImplementedException();

	public Task UpsertPackageRemoteAsync(PackageRemoteRecord remote)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<PackageRemoteRecord>> GetPackageRemotesAsync()
		=> throw new NotImplementedException();

	public Task<OneOf<PackageRemoteRecord, NotFound>> GetPackageRemoteAsync(string name)
		=> throw new NotImplementedException();

	public Task RemovePackageRemoteAsync(string name)
		=> throw new NotImplementedException();

	public Task AddPackageRevisionAsync(PackageRevisionRecord revision)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<PackageRevisionRecord>> GetPackageRevisionsAsync(string packageId)
		=> throw new NotImplementedException();

	public Task<OneOf<PackageRevisionRecord, NotFound>> GetPackageRevisionAsync(string packageId, int revision)
		=> throw new NotImplementedException();

	public Task PrunePackageRevisionsAsync(string packageId, int keep)
		=> throw new NotImplementedException();
}
