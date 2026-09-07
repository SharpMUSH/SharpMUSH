using System.Globalization;
using OneOf;
using OneOf.Types;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using Records = SharpMUSH.Database.Lightning.Records;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.IPackageRegistryService"/>: the softcode package registry.
/// Ported from <c>SurrealDatabase.Packages.cs</c>. The storage records under
/// <c>SharpMUSH.Database.Lightning.Records</c> share their names with this interface's own model
/// types (both mirror the SurrealDB <c>SysXyzDbRecord</c> shapes 1:1), so every storage-side
/// reference below is qualified through the <c>Records</c> alias to keep the two apart.
/// </summary>
/// <remarks>
/// Composite identities match the SurrealDB partial's <c>type::thing</c> keys, except
/// <see cref="Tables.PkgDep"/>: Lightning models a dependency edge as a native LMDB dup-sort entry
/// (key = owning package id, one duplicate value per dependency) rather than fabricating a composite
/// id, so <see cref="GetPackageDependentsAsync"/> — the reverse direction — scans the table and
/// filters, same as <see cref="GetAccountIdsForRoleAsync"/> does for role assignments.
/// </remarks>
public partial class LightningDatabase
{
	private static DateTimeOffset ParsePackageTimestamp(string iso)
		=> DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

	private static string FormatPackageTimestamp(DateTimeOffset value)
		=> value.ToString("o", CultureInfo.InvariantCulture);

	#region Installed Packages

	private static Records.InstalledPackageRecord ToInstalledPackageRecord(InstalledPackageRecord package) => new()
	{
		PackageId = package.Id,
		Version = package.Version,
		SourceRepo = package.SourceRepo,
		SourcePath = package.SourcePath,
		InstalledCommit = package.InstalledCommit,
		PinnedBranch = package.PinnedBranch,
		InstalledAt = FormatPackageTimestamp(package.InstalledAt),
		CurrentRevision = package.CurrentRevision,
		DeployedFiles = package.DeployedFiles?.ToArray()
	};

	private static InstalledPackageRecord MapInstalledPackage(Records.InstalledPackageRecord r) => new(
		r.PackageId, r.Version, r.SourceRepo, r.SourcePath, r.InstalledCommit, r.PinnedBranch,
		ParsePackageTimestamp(r.InstalledAt), r.CurrentRevision,
		r.DeployedFiles is { Length: > 0 } ? r.DeployedFiles : null);

	public async Task UpsertInstalledPackageAsync(InstalledPackageRecord package)
		=> await Store.WriteAsync(tx =>
			tx.Put(Tables.Pkg, Keys.Str(package.Id), Codec.Serialize(ToInstalledPackageRecord(package))));

	public Task<OneOf<InstalledPackageRecord, NotFound>> GetInstalledPackageAsync(string packageId)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.Pkg, Keys.Str(packageId), out var bytes)
			? MapInstalledPackage(Codec.Deserialize<Records.InstalledPackageRecord>(bytes))
			: (InstalledPackageRecord?)null);
		return Task.FromResult<OneOf<InstalledPackageRecord, NotFound>>(result is null ? new NotFound() : result);
	}

	public Task<IReadOnlyList<InstalledPackageRecord>> GetInstalledPackagesAsync()
	{
		var results = Store.Read(tx => tx.Range(Tables.Pkg, [])
			.Select(e => MapInstalledPackage(Codec.Deserialize<Records.InstalledPackageRecord>(e.Value)))
			.OrderBy(p => p.Id, StringComparer.Ordinal)
			.ToList());
		return Task.FromResult<IReadOnlyList<InstalledPackageRecord>>(results);
	}

	public async Task RemoveInstalledPackageAsync(string packageId)
	{
		await Store.WriteAsync(tx =>
		{
			tx.DeletePrefix(Tables.PkgObj, Keys.Composite(packageId, ""));
			tx.DeletePrefix(Tables.PkgAttr, Keys.Composite(packageId, ""));
			tx.DeletePrefix(Tables.PkgStruct, Keys.Composite(packageId, ""));
			tx.DeletePrefix(Tables.PkgRev, Keys.Composite(packageId, ""));

			// Dependency edges in both directions: outbound is an exact-key delete (all dups under
			// this package's own key); inbound requires a scan since the value, not the key, carries
			// the dependsOnId that other packages' entries point at.
			tx.Delete(Tables.PkgDep, Keys.Str(packageId));
			foreach (var (key, value) in tx.Range(Tables.PkgDep, []).ToList())
			{
				var dependency = Codec.Deserialize<Records.PackageDependencyRecord>(value);
				if (dependency.DependsOnId == packageId)
				{
					tx.Delete(Tables.PkgDep, key, value);
				}
			}

			tx.Delete(Tables.Pkg, Keys.Str(packageId));
		});
	}

	#endregion

	#region Package Objects

	public async Task UpsertPackageObjectAsync(PackageObjectRecord record)
	{
		var stored = new Records.PackageObjectRecord
		{
			PackageId = record.PackageId,
			RefName = record.Ref,
			Objid = record.Objid,
			ObjectType = record.Type
		};
		await Store.WriteAsync(tx =>
			tx.Put(Tables.PkgObj, Keys.Composite(record.PackageId, record.Ref), Codec.Serialize(stored)));
	}

	public Task<IReadOnlyList<PackageObjectRecord>> GetPackageObjectsAsync(string packageId)
	{
		var results = Store.Read(tx => tx.Range(Tables.PkgObj, Keys.Composite(packageId, ""))
			.Select(e => Codec.Deserialize<Records.PackageObjectRecord>(e.Value))
			.OrderBy(r => r.RefName, StringComparer.Ordinal)
			.Select(r => new PackageObjectRecord(r.PackageId, r.RefName, r.Objid, r.ObjectType))
			.ToList());
		return Task.FromResult<IReadOnlyList<PackageObjectRecord>>(results);
	}

	public async Task RemovePackageObjectAsync(string packageId, string @ref)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.PkgObj, Keys.Composite(packageId, @ref)));

	#endregion

	#region Managed Attributes

	public async Task UpsertManagedAttributeAsync(ManagedAttributeRecord record)
	{
		var stored = new Records.ManagedAttributeRecord
		{
			PackageId = record.PackageId,
			Objid = record.Objid,
			Attribute = record.Attribute,
			BaselineValue = record.BaselineValue,
			BaselineHash = record.BaselineHash,
			BaselineVersion = record.BaselineVersion
		};
		await Store.WriteAsync(tx =>
			tx.Put(Tables.PkgAttr, Keys.Composite(record.PackageId, record.Objid, record.Attribute), Codec.Serialize(stored)));
	}

	private static ManagedAttributeRecord MapManagedAttribute(Records.ManagedAttributeRecord r) => new(
		r.PackageId, r.Objid, r.Attribute, r.BaselineValue, r.BaselineHash, r.BaselineVersion);

	public Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesAsync(string packageId)
	{
		var results = Store.Read(tx => tx.Range(Tables.PkgAttr, Keys.Composite(packageId, ""))
			.Select(e => Codec.Deserialize<Records.ManagedAttributeRecord>(e.Value))
			.OrderBy(r => r.Objid, StringComparer.Ordinal)
			.ThenBy(r => r.Attribute, StringComparer.Ordinal)
			.Select(MapManagedAttribute)
			.ToList());
		return Task.FromResult<IReadOnlyList<ManagedAttributeRecord>>(results);
	}

	public Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesForObjectAsync(string objid)
	{
		var results = Store.Read(tx => tx.Range(Tables.PkgAttr, [])
			.Select(e => Codec.Deserialize<Records.ManagedAttributeRecord>(e.Value))
			.Where(r => r.Objid == objid)
			.OrderBy(r => r.Attribute, StringComparer.Ordinal)
			.Select(MapManagedAttribute)
			.ToList());
		return Task.FromResult<IReadOnlyList<ManagedAttributeRecord>>(results);
	}

	public async Task RemoveManagedAttributeAsync(string packageId, string objid, string attribute)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.PkgAttr, Keys.Composite(packageId, objid, attribute)));

	#endregion

	#region Managed Structures

	public async Task UpsertManagedStructureAsync(ManagedStructureRecord record)
	{
		var stored = new Records.ManagedStructureRecord
		{
			PackageId = record.PackageId,
			Objid = record.Objid,
			StructureJson = record.StructureJson,
			BaselineVersion = record.BaselineVersion
		};
		await Store.WriteAsync(tx =>
			tx.Put(Tables.PkgStruct, Keys.Composite(record.PackageId, record.Objid), Codec.Serialize(stored)));
	}

	public Task<IReadOnlyList<ManagedStructureRecord>> GetManagedStructuresAsync(string packageId)
	{
		var results = Store.Read(tx => tx.Range(Tables.PkgStruct, Keys.Composite(packageId, ""))
			.Select(e => Codec.Deserialize<Records.ManagedStructureRecord>(e.Value))
			.OrderBy(r => r.Objid, StringComparer.Ordinal)
			.Select(r => new ManagedStructureRecord(r.PackageId, r.Objid, r.StructureJson, r.BaselineVersion))
			.ToList());
		return Task.FromResult<IReadOnlyList<ManagedStructureRecord>>(results);
	}

	public async Task RemoveManagedStructureAsync(string packageId, string objid)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.PkgStruct, Keys.Composite(packageId, objid)));

	#endregion

	#region Dependencies

	public async Task SetPackageDependenciesAsync(string packageId, IReadOnlyList<PackageDependencyRecord> dependencies)
	{
		await Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.PkgDep, Keys.Str(packageId));
			foreach (var dependency in dependencies)
			{
				var stored = new Records.PackageDependencyRecord
				{
					PackageId = packageId,
					DependsOnId = dependency.DependsOnId,
					Constraint = dependency.Constraint
				};
				tx.Put(Tables.PkgDep, Keys.Str(packageId), Codec.Serialize(stored));
			}
		});
	}

	private static PackageDependencyRecord MapDependency(Records.PackageDependencyRecord r) => new(
		r.PackageId, r.DependsOnId, r.Constraint);

	public Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependenciesAsync(string packageId)
	{
		var results = Store.Read(tx => tx.Dups(Tables.PkgDep, Keys.Str(packageId))
			.Select(v => Codec.Deserialize<Records.PackageDependencyRecord>(v))
			.OrderBy(r => r.DependsOnId, StringComparer.Ordinal)
			.Select(MapDependency)
			.ToList());
		return Task.FromResult<IReadOnlyList<PackageDependencyRecord>>(results);
	}

	public Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependentsAsync(string packageId)
	{
		var results = Store.Read(tx => tx.Range(Tables.PkgDep, [])
			.Select(e => Codec.Deserialize<Records.PackageDependencyRecord>(e.Value))
			.Where(r => r.DependsOnId == packageId)
			.OrderBy(r => r.PackageId, StringComparer.Ordinal)
			.Select(MapDependency)
			.ToList());
		return Task.FromResult<IReadOnlyList<PackageDependencyRecord>>(results);
	}

	#endregion

	#region Remotes

	public async Task UpsertPackageRemoteAsync(PackageRemoteRecord remote)
	{
		var stored = new Records.PackageRemoteRecord
		{
			Name = remote.Name,
			Url = remote.Url,
			Trust = remote.Trust.ToString(),
			Branch = remote.Branch
		};
		await Store.WriteAsync(tx => tx.Put(Tables.PkgRemote, Keys.Str(remote.Name), Codec.Serialize(stored)));
	}

	private static PackageRemoteRecord MapRemote(Records.PackageRemoteRecord r) => new(
		r.Name, r.Url, Enum.Parse<PackageRemoteTrust>(r.Trust, ignoreCase: true), r.Branch);

	public Task<IReadOnlyList<PackageRemoteRecord>> GetPackageRemotesAsync()
	{
		var results = Store.Read(tx => tx.Range(Tables.PkgRemote, [])
			.Select(e => Codec.Deserialize<Records.PackageRemoteRecord>(e.Value))
			.OrderBy(r => r.Name, StringComparer.Ordinal)
			.Select(MapRemote)
			.ToList());
		return Task.FromResult<IReadOnlyList<PackageRemoteRecord>>(results);
	}

	public Task<OneOf<PackageRemoteRecord, NotFound>> GetPackageRemoteAsync(string name)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.PkgRemote, Keys.Str(name), out var bytes)
			? MapRemote(Codec.Deserialize<Records.PackageRemoteRecord>(bytes))
			: (PackageRemoteRecord?)null);
		return Task.FromResult<OneOf<PackageRemoteRecord, NotFound>>(result is null ? new NotFound() : result);
	}

	public async Task RemovePackageRemoteAsync(string name)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.PkgRemote, Keys.Str(name)));

	#endregion

	#region Revisions

	public async Task AddPackageRevisionAsync(PackageRevisionRecord revision)
	{
		var stored = new Records.PackageRevisionRecord
		{
			PackageId = revision.PackageId,
			Revision = revision.Revision,
			Kind = revision.Kind.ToString(),
			Version = revision.Version,
			Commit = revision.Commit,
			ManifestSnapshotJson = revision.ManifestSnapshotJson,
			ConfigureAnswersJson = revision.ConfigureAnswersJson,
			PreApplyValuesJson = revision.PreApplyValuesJson,
			AppliedAt = FormatPackageTimestamp(revision.AppliedAt)
		};
		await Store.WriteAsync(tx =>
			tx.Put(Tables.PkgRev, Keys.Composite(revision.PackageId, (long)revision.Revision), Codec.Serialize(stored)));
	}

	private static PackageRevisionRecord MapRevision(Records.PackageRevisionRecord r) => new(
		r.PackageId, r.Revision, Enum.Parse<PackageRevisionKind>(r.Kind, ignoreCase: true),
		r.Version, r.Commit, r.ManifestSnapshotJson, r.ConfigureAnswersJson, r.PreApplyValuesJson,
		ParsePackageTimestamp(r.AppliedAt));

	public Task<IReadOnlyList<PackageRevisionRecord>> GetPackageRevisionsAsync(string packageId)
	{
		var results = Store.Read(tx => tx.Range(Tables.PkgRev, Keys.Composite(packageId, ""))
			.Select(e => Codec.Deserialize<Records.PackageRevisionRecord>(e.Value))
			.OrderByDescending(r => r.Revision)
			.Select(MapRevision)
			.ToList());
		return Task.FromResult<IReadOnlyList<PackageRevisionRecord>>(results);
	}

	public Task<OneOf<PackageRevisionRecord, NotFound>> GetPackageRevisionAsync(string packageId, int revision)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.PkgRev, Keys.Composite(packageId, (long)revision), out var bytes)
			? MapRevision(Codec.Deserialize<Records.PackageRevisionRecord>(bytes))
			: (PackageRevisionRecord?)null);
		return Task.FromResult<OneOf<PackageRevisionRecord, NotFound>>(result is null ? new NotFound() : result);
	}

	public async Task PrunePackageRevisionsAsync(string packageId, int keep)
	{
		await Store.WriteAsync(tx =>
		{
			var toPrune = tx.Range(Tables.PkgRev, Keys.Composite(packageId, ""))
				.Select(e => Codec.Deserialize<Records.PackageRevisionRecord>(e.Value))
				.OrderByDescending(r => r.Revision)
				.Skip(keep)
				.ToList();

			foreach (var revision in toPrune)
			{
				tx.Delete(Tables.PkgRev, Keys.Composite(packageId, (long)revision.Revision));
			}
		});
	}

	#endregion
}
