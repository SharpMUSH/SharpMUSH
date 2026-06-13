using System.Globalization;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

// IMPORTANT: SurrealDb.Net's embedded CBOR serializer ignores [JsonPropertyName].
// Property names MUST exactly match the SurrealDB field names stored in the DB —
// hence the camelCase property names on these records.
internal class SysPackageDbRecord : Record
{
	public string packageId { get; set; } = "";
	public string version { get; set; } = "";
	public string sourceRepo { get; set; } = "";
	public string? sourcePath { get; set; }
	public string installedCommit { get; set; } = "";
	public string? pinnedBranch { get; set; }
	public string installedAt { get; set; } = "";
	public int currentRevision { get; set; }
}

internal class SysPackageObjectDbRecord : Record
{
	public string packageId { get; set; } = "";
	public string refName { get; set; } = "";
	public string objid { get; set; } = "";
	public string objectType { get; set; } = "";
}

internal class SysManagedAttributeDbRecord : Record
{
	public string packageId { get; set; } = "";
	public string objid { get; set; } = "";
	public string attribute { get; set; } = "";
	public string baselineValue { get; set; } = "";
	public string baselineHash { get; set; } = "";
	public string baselineVersion { get; set; } = "";
}

internal class SysManagedStructureDbRecord : Record
{
	public string packageId { get; set; } = "";
	public string objid { get; set; } = "";
	public string structureJson { get; set; } = "";
	public string baselineVersion { get; set; } = "";
}

internal class SysPackageDependencyDbRecord : Record
{
	public string packageId { get; set; } = "";
	public string dependsOnId { get; set; } = "";
	public string constraint { get; set; } = "";
}

internal class SysRemoteDbRecord : Record
{
	public string name { get; set; } = "";
	public string url { get; set; } = "";
	public string trust { get; set; } = "";
	public string? branch { get; set; }
}

internal class SysPackageRevisionDbRecord : Record
{
	public string packageId { get; set; } = "";
	public int revision { get; set; }
	public string kind { get; set; } = "";
	public string version { get; set; } = "";
	public string commit { get; set; } = "";
	public string manifestSnapshotJson { get; set; } = "";
	public string configureAnswersJson { get; set; } = "";
	public string preApplyValuesJson { get; set; } = "";
	public string appliedAt { get; set; } = "";
}

public partial class SurrealDatabase : IPackageRegistryService
{
	#region Package Registry

	private const string SysPackageFields =
		"id, packageId, version, sourceRepo, sourcePath, installedCommit, pinnedBranch, installedAt, currentRevision";

	private const string SysPackageObjectFields = "id, packageId, refName, objid, objectType";

	private const string SysManagedAttributeFields =
		"id, packageId, objid, attribute, baselineValue, baselineHash, baselineVersion";

	private const string SysManagedStructureFields =
		"id, packageId, objid, structureJson, baselineVersion";

	private const string SysPackageDependencyFields = "id, packageId, dependsOnId, constraint";

	private const string SysRemoteFields = "id, name, url, trust, branch";

	private const string SysPackageRevisionFields =
		"id, packageId, revision, kind, version, commit, manifestSnapshotJson, configureAnswersJson, preApplyValuesJson, appliedAt";

	private static DateTimeOffset ParsePackageTimestamp(string iso) =>
		DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

	// ── Installed packages ─────────────────────────────────────────────────

	public async Task UpsertInstalledPackageAsync(InstalledPackageRecord package)
	{
		var parameters = new Dictionary<string, object?>
		{
			["pkg"] = package.Id,
			["version"] = package.Version,
			["sourceRepo"] = package.SourceRepo,
			["sourcePath"] = package.SourcePath,
			["installedCommit"] = package.InstalledCommit,
			["pinnedBranch"] = package.PinnedBranch,
			["installedAt"] = package.InstalledAt.ToString("o", CultureInfo.InvariantCulture),
			["currentRevision"] = package.CurrentRevision
		};
		await ExecuteAsync("""
			UPSERT type::thing('sys_package', $pkg) SET packageId = $pkg, version = $version,
				sourceRepo = $sourceRepo, sourcePath = $sourcePath, installedCommit = $installedCommit,
				pinnedBranch = $pinnedBranch, installedAt = $installedAt, currentRevision = $currentRevision
			""", parameters);
	}

	public async Task<OneOf<InstalledPackageRecord, NotFound>> GetInstalledPackageAsync(string packageId)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysPackageFields} FROM sys_package WHERE packageId = $pkg",
			new Dictionary<string, object?> { ["pkg"] = packageId });
		var results = response.GetValue<List<SysPackageDbRecord>>(0);

		return results?.Count > 0 ? MapPackage(results[0]) : new NotFound();
	}

	public async Task<IReadOnlyList<InstalledPackageRecord>> GetInstalledPackagesAsync()
	{
		var response = await ExecuteAsync($"SELECT {SysPackageFields} FROM sys_package ORDER BY packageId");
		var results = response.GetValue<List<SysPackageDbRecord>>(0) ?? [];
		return results.Select(MapPackage).ToList();
	}

	private static InstalledPackageRecord MapPackage(SysPackageDbRecord record) => new(
		record.packageId, record.version, record.sourceRepo, record.sourcePath,
		record.installedCommit, record.pinnedBranch, ParsePackageTimestamp(record.installedAt),
		record.currentRevision);

	public async Task RemoveInstalledPackageAsync(string packageId)
	{
		var parameters = new Dictionary<string, object?> { ["pkg"] = packageId };
		await ExecuteAsync("DELETE sys_package_object WHERE packageId = $pkg", parameters);
		await ExecuteAsync("DELETE sys_managed_attribute WHERE packageId = $pkg", parameters);
		await ExecuteAsync("DELETE sys_managed_structure WHERE packageId = $pkg", parameters);
		await ExecuteAsync("DELETE sys_package_revision WHERE packageId = $pkg", parameters);
		await ExecuteAsync("DELETE sys_package_dependency WHERE packageId = $pkg OR dependsOnId = $pkg", parameters);
		await ExecuteAsync("DELETE type::thing('sys_package', $pkg)", parameters);
	}

	// ── Package-created objects ────────────────────────────────────────────

	public async Task UpsertPackageObjectAsync(PackageObjectRecord record)
	{
		var parameters = new Dictionary<string, object?>
		{
			["key"] = $"{record.PackageId}/{record.Ref}",
			["pkg"] = record.PackageId,
			["refName"] = record.Ref,
			["objid"] = record.Objid,
			["objectType"] = record.Type
		};
		await ExecuteAsync("""
			UPSERT type::thing('sys_package_object', $key) SET packageId = $pkg, refName = $refName,
				objid = $objid, objectType = $objectType
			""", parameters);
	}

	public async Task<IReadOnlyList<PackageObjectRecord>> GetPackageObjectsAsync(string packageId)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysPackageObjectFields} FROM sys_package_object WHERE packageId = $pkg ORDER BY refName",
			new Dictionary<string, object?> { ["pkg"] = packageId });
		var results = response.GetValue<List<SysPackageObjectDbRecord>>(0) ?? [];

		return results
			.Select(r => new PackageObjectRecord(r.packageId, r.refName, r.objid, r.objectType))
			.ToList();
	}

	public async Task RemovePackageObjectAsync(string packageId, string @ref)
	{
		await ExecuteAsync("DELETE type::thing('sys_package_object', $key)",
			new Dictionary<string, object?> { ["key"] = $"{packageId}/{@ref}" });
	}

	// ── Managed attributes ─────────────────────────────────────────────────

	public async Task UpsertManagedAttributeAsync(ManagedAttributeRecord record)
	{
		var parameters = new Dictionary<string, object?>
		{
			["key"] = $"{record.PackageId}/{record.Objid}/{record.Attribute}",
			["pkg"] = record.PackageId,
			["objid"] = record.Objid,
			["attribute"] = record.Attribute,
			["baselineValue"] = record.BaselineValue,
			["baselineHash"] = record.BaselineHash,
			["baselineVersion"] = record.BaselineVersion
		};
		await ExecuteAsync("""
			UPSERT type::thing('sys_managed_attribute', $key) SET packageId = $pkg, objid = $objid,
				attribute = $attribute, baselineValue = $baselineValue, baselineHash = $baselineHash,
				baselineVersion = $baselineVersion
			""", parameters);
	}

	public async Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesAsync(string packageId)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysManagedAttributeFields} FROM sys_managed_attribute WHERE packageId = $pkg ORDER BY objid, attribute",
			new Dictionary<string, object?> { ["pkg"] = packageId });
		var results = response.GetValue<List<SysManagedAttributeDbRecord>>(0) ?? [];
		return results.Select(MapManagedAttribute).ToList();
	}

	public async Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesForObjectAsync(string objid)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysManagedAttributeFields} FROM sys_managed_attribute WHERE objid = $objid ORDER BY attribute",
			new Dictionary<string, object?> { ["objid"] = objid });
		var results = response.GetValue<List<SysManagedAttributeDbRecord>>(0) ?? [];
		return results.Select(MapManagedAttribute).ToList();
	}

	private static ManagedAttributeRecord MapManagedAttribute(SysManagedAttributeDbRecord r) => new(
		r.packageId, r.objid, r.attribute, r.baselineValue, r.baselineHash, r.baselineVersion);

	public async Task RemoveManagedAttributeAsync(string packageId, string objid, string attribute)
	{
		await ExecuteAsync("DELETE type::thing('sys_managed_attribute', $key)",
			new Dictionary<string, object?> { ["key"] = $"{packageId}/{objid}/{attribute}" });
	}

	// ── Managed object structure ────────────────────────────────────────────

	public async Task UpsertManagedStructureAsync(ManagedStructureRecord record)
	{
		var parameters = new Dictionary<string, object?>
		{
			["key"] = $"{record.PackageId}/{record.Objid}",
			["pkg"] = record.PackageId,
			["objid"] = record.Objid,
			["structureJson"] = record.StructureJson,
			["baselineVersion"] = record.BaselineVersion
		};
		await ExecuteAsync("""
			UPSERT type::thing('sys_managed_structure', $key) SET packageId = $pkg, objid = $objid,
				structureJson = $structureJson, baselineVersion = $baselineVersion
			""", parameters);
	}

	public async Task<IReadOnlyList<ManagedStructureRecord>> GetManagedStructuresAsync(string packageId)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysManagedStructureFields} FROM sys_managed_structure WHERE packageId = $pkg ORDER BY objid",
			new Dictionary<string, object?> { ["pkg"] = packageId });
		var results = response.GetValue<List<SysManagedStructureDbRecord>>(0) ?? [];
		return results
			.Select(r => new ManagedStructureRecord(r.packageId, r.objid, r.structureJson, r.baselineVersion))
			.ToList();
	}

	public async Task RemoveManagedStructureAsync(string packageId, string objid)
	{
		await ExecuteAsync("DELETE type::thing('sys_managed_structure', $key)",
			new Dictionary<string, object?> { ["key"] = $"{packageId}/{objid}" });
	}

	// ── Dependencies ───────────────────────────────────────────────────────

	public async Task SetPackageDependenciesAsync(string packageId, IReadOnlyList<PackageDependencyRecord> dependencies)
	{
		await ExecuteAsync("DELETE sys_package_dependency WHERE packageId = $pkg",
			new Dictionary<string, object?> { ["pkg"] = packageId });

		foreach (var dependency in dependencies)
		{
			var parameters = new Dictionary<string, object?>
			{
				["key"] = $"{packageId}/{dependency.DependsOnId}",
				["pkg"] = packageId,
				["dependsOnId"] = dependency.DependsOnId,
				["constraint"] = dependency.Constraint
			};
			await ExecuteAsync("""
				UPSERT type::thing('sys_package_dependency', $key) SET packageId = $pkg,
					dependsOnId = $dependsOnId, constraint = $constraint
				""", parameters);
		}
	}

	public async Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependenciesAsync(string packageId)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysPackageDependencyFields} FROM sys_package_dependency WHERE packageId = $pkg ORDER BY dependsOnId",
			new Dictionary<string, object?> { ["pkg"] = packageId });
		var results = response.GetValue<List<SysPackageDependencyDbRecord>>(0) ?? [];
		return results.Select(MapDependency).ToList();
	}

	public async Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependentsAsync(string packageId)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysPackageDependencyFields} FROM sys_package_dependency WHERE dependsOnId = $pkg ORDER BY packageId",
			new Dictionary<string, object?> { ["pkg"] = packageId });
		var results = response.GetValue<List<SysPackageDependencyDbRecord>>(0) ?? [];
		return results.Select(MapDependency).ToList();
	}

	private static PackageDependencyRecord MapDependency(SysPackageDependencyDbRecord r) => new(
		r.packageId, r.dependsOnId, r.constraint);

	// ── Remotes ────────────────────────────────────────────────────────────

	public async Task UpsertPackageRemoteAsync(PackageRemoteRecord remote)
	{
		var parameters = new Dictionary<string, object?>
		{
			["name"] = remote.Name,
			["url"] = remote.Url,
			["trust"] = remote.Trust.ToString().ToLowerInvariant(),
			["branch"] = remote.Branch
		};
		await ExecuteAsync("""
			UPSERT type::thing('sys_remote', $name) SET name = $name, url = $url, trust = $trust, branch = $branch
			""", parameters);
	}

	public async Task<IReadOnlyList<PackageRemoteRecord>> GetPackageRemotesAsync()
	{
		var response = await ExecuteAsync($"SELECT {SysRemoteFields} FROM sys_remote ORDER BY name");
		var results = response.GetValue<List<SysRemoteDbRecord>>(0) ?? [];
		return results.Select(MapRemote).ToList();
	}

	public async Task<OneOf<PackageRemoteRecord, NotFound>> GetPackageRemoteAsync(string name)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysRemoteFields} FROM sys_remote WHERE name = $name",
			new Dictionary<string, object?> { ["name"] = name });
		var results = response.GetValue<List<SysRemoteDbRecord>>(0);

		return results?.Count > 0 ? MapRemote(results[0]) : new NotFound();
	}

	private static PackageRemoteRecord MapRemote(SysRemoteDbRecord r) => new(
		r.name, r.url, Enum.Parse<PackageRemoteTrust>(r.trust, ignoreCase: true), r.branch);

	public async Task RemovePackageRemoteAsync(string name)
	{
		await ExecuteAsync("DELETE type::thing('sys_remote', $name)",
			new Dictionary<string, object?> { ["name"] = name });
	}

	// ── Revisions ──────────────────────────────────────────────────────────

	public async Task AddPackageRevisionAsync(PackageRevisionRecord revision)
	{
		var parameters = new Dictionary<string, object?>
		{
			["key"] = $"{revision.PackageId}/{revision.Revision}",
			["pkg"] = revision.PackageId,
			["revision"] = revision.Revision,
			["kind"] = revision.Kind.ToString().ToLowerInvariant(),
			["version"] = revision.Version,
			["commit"] = revision.Commit,
			["manifestSnapshotJson"] = revision.ManifestSnapshotJson,
			["configureAnswersJson"] = revision.ConfigureAnswersJson,
			["preApplyValuesJson"] = revision.PreApplyValuesJson,
			["appliedAt"] = revision.AppliedAt.ToString("o", CultureInfo.InvariantCulture)
		};
		await ExecuteAsync("""
			UPSERT type::thing('sys_package_revision', $key) SET packageId = $pkg, revision = $revision,
				kind = $kind, version = $version, commit = $commit, manifestSnapshotJson = $manifestSnapshotJson,
				configureAnswersJson = $configureAnswersJson, preApplyValuesJson = $preApplyValuesJson,
				appliedAt = $appliedAt
			""", parameters);
	}

	public async Task<IReadOnlyList<PackageRevisionRecord>> GetPackageRevisionsAsync(string packageId)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysPackageRevisionFields} FROM sys_package_revision WHERE packageId = $pkg ORDER BY revision DESC",
			new Dictionary<string, object?> { ["pkg"] = packageId });
		var results = response.GetValue<List<SysPackageRevisionDbRecord>>(0) ?? [];
		return results.Select(MapRevision).ToList();
	}

	public async Task<OneOf<PackageRevisionRecord, NotFound>> GetPackageRevisionAsync(string packageId, int revision)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysPackageRevisionFields} FROM sys_package_revision WHERE packageId = $pkg AND revision = $rev",
			new Dictionary<string, object?> { ["pkg"] = packageId, ["rev"] = revision });
		var results = response.GetValue<List<SysPackageRevisionDbRecord>>(0);

		return results?.Count > 0 ? MapRevision(results[0]) : new NotFound();
	}

	private static PackageRevisionRecord MapRevision(SysPackageRevisionDbRecord r) => new(
		r.packageId, r.revision, Enum.Parse<PackageRevisionKind>(r.kind, ignoreCase: true),
		r.version, r.commit, r.manifestSnapshotJson, r.configureAnswersJson, r.preApplyValuesJson,
		ParsePackageTimestamp(r.appliedAt));

	public async Task PrunePackageRevisionsAsync(string packageId, int keep)
	{
		await ExecuteAsync("""
			DELETE sys_package_revision WHERE packageId = $pkg AND revision NOT IN
				(SELECT VALUE revision FROM sys_package_revision WHERE packageId = $pkg ORDER BY revision DESC LIMIT $keep)
			""",
			new Dictionary<string, object?> { ["pkg"] = packageId, ["keep"] = keep });
	}

	#endregion
}
