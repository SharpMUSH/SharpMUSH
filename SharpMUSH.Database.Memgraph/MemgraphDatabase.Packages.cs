using System.Globalization;
using Neo4j.Driver;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase : IPackageRegistryService
{
	#region Package Registry

	// Dependency edges require both packages to exist (the plan engine
	// guarantees dependencies are installed first).

	private static string? OptionalString(INode node, string property) =>
		node.Properties.TryGetValue(property, out var value) && value is not null ? value.As<string>() : null;

	private static DateTimeOffset ParsePackageTimestamp(string iso) =>
		DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

	public async Task UpsertInstalledPackageAsync(InstalledPackageRecord package)
	{
		await ExecuteWithRetryAsync("""
			MERGE (p:SysPackage {id: $id})
			SET p.version = $version, p.sourceRepo = $sourceRepo, p.sourcePath = $sourcePath,
			    p.installedCommit = $installedCommit, p.pinnedBranch = $pinnedBranch,
			    p.installedAt = $installedAt, p.currentRevision = $currentRevision,
			    p.deployedFiles = $deployedFiles
			""",
			new
			{
				id = package.Id,
				version = package.Version,
				sourceRepo = package.SourceRepo,
				sourcePath = package.SourcePath,
				installedCommit = package.InstalledCommit,
				pinnedBranch = package.PinnedBranch,
				installedAt = package.InstalledAt.ToString("o", CultureInfo.InvariantCulture),
				currentRevision = package.CurrentRevision,
				deployedFiles = (package.DeployedFiles ?? []).ToArray()
			});
	}

	public async Task<OneOf<InstalledPackageRecord, NotFound>> GetInstalledPackageAsync(string packageId)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (p:SysPackage {id: $id}) RETURN p", new { id = packageId });

		return result.Result.Count == 0
			? new NotFound()
			: MapPackageNode(result.Result[0]["p"].As<INode>());
	}

	public async Task<IReadOnlyList<InstalledPackageRecord>> GetInstalledPackagesAsync()
	{
		var result = await ExecuteWithRetryAsync("MATCH (p:SysPackage) RETURN p ORDER BY p.id");
		return result.Result.Select(r => MapPackageNode(r["p"].As<INode>())).ToList();
	}

	private static InstalledPackageRecord MapPackageNode(INode node) => new(
		node.Properties["id"].As<string>(),
		node.Properties["version"].As<string>(),
		node.Properties["sourceRepo"].As<string>(),
		OptionalString(node, "sourcePath"),
		node.Properties["installedCommit"].As<string>(),
		OptionalString(node, "pinnedBranch"),
		ParsePackageTimestamp(node.Properties["installedAt"].As<string>()),
		node.Properties["currentRevision"].As<int>(),
		OptionalStringList(node, "deployedFiles"));

	private static IReadOnlyList<string>? OptionalStringList(INode node, string property)
	{
		if (!node.Properties.TryGetValue(property, out var value) || value is null)
		{
			return null;
		}

		var list = value.As<List<object>>().Select(o => o.As<string>()).ToList();
		return list.Count > 0 ? list : null;
	}

	public async Task RemoveInstalledPackageAsync(string packageId)
	{
		await ExecuteWithRetryAsync(
			"MATCH (d:SysPackageObject {packageId: $id}) DETACH DELETE d", new { id = packageId });
		await ExecuteWithRetryAsync(
			"MATCH (d:SysManagedAttribute {packageId: $id}) DETACH DELETE d", new { id = packageId });
		await ExecuteWithRetryAsync(
			"MATCH (d:SysManagedStructure {packageId: $id}) DETACH DELETE d", new { id = packageId });
		await ExecuteWithRetryAsync(
			"MATCH (d:SysPackageRevision {packageId: $id}) DETACH DELETE d", new { id = packageId });
		await ExecuteWithRetryAsync(
			"MATCH (p:SysPackage {id: $id}) DETACH DELETE p", new { id = packageId });
	}

	public async Task UpsertPackageObjectAsync(PackageObjectRecord record)
	{
		await ExecuteWithRetryAsync("""
			MERGE (d:SysPackageObject {packageId: $packageId, ref: $ref})
			SET d.objid = $objid, d.type = $type
			""",
			new { packageId = record.PackageId, @ref = record.Ref, objid = record.Objid, type = record.Type });
	}

	public async Task<IReadOnlyList<PackageObjectRecord>> GetPackageObjectsAsync(string packageId)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (d:SysPackageObject {packageId: $id}) RETURN d ORDER BY d.ref", new { id = packageId });

		return result.Result.Select(r =>
		{
			var node = r["d"].As<INode>();
			return new PackageObjectRecord(
				node.Properties["packageId"].As<string>(),
				node.Properties["ref"].As<string>(),
				node.Properties["objid"].As<string>(),
				node.Properties["type"].As<string>());
		}).ToList();
	}

	public async Task RemovePackageObjectAsync(string packageId, string @ref)
	{
		await ExecuteWithRetryAsync(
			"MATCH (d:SysPackageObject {packageId: $id, ref: $ref}) DETACH DELETE d",
			new { id = packageId, @ref });
	}

	public async Task UpsertManagedAttributeAsync(ManagedAttributeRecord record)
	{
		await ExecuteWithRetryAsync("""
			MERGE (d:SysManagedAttribute {packageId: $packageId, objid: $objid, attribute: $attribute})
			SET d.baselineValue = $baselineValue, d.baselineHash = $baselineHash, d.baselineVersion = $baselineVersion
			""",
			new
			{
				packageId = record.PackageId,
				objid = record.Objid,
				attribute = record.Attribute,
				baselineValue = record.BaselineValue,
				baselineHash = record.BaselineHash,
				baselineVersion = record.BaselineVersion
			});
	}

	public async Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesAsync(string packageId)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (d:SysManagedAttribute {packageId: $id}) RETURN d ORDER BY d.objid, d.attribute",
			new { id = packageId });

		return result.Result.Select(r => MapManagedAttributeNode(r["d"].As<INode>())).ToList();
	}

	public async Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesForObjectAsync(string objid)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (d:SysManagedAttribute {objid: $objid}) RETURN d ORDER BY d.attribute",
			new { objid });

		return result.Result.Select(r => MapManagedAttributeNode(r["d"].As<INode>())).ToList();
	}

	private static ManagedAttributeRecord MapManagedAttributeNode(INode node) => new(
		node.Properties["packageId"].As<string>(),
		node.Properties["objid"].As<string>(),
		node.Properties["attribute"].As<string>(),
		node.Properties["baselineValue"].As<string>(),
		node.Properties["baselineHash"].As<string>(),
		node.Properties["baselineVersion"].As<string>());

	public async Task RemoveManagedAttributeAsync(string packageId, string objid, string attribute)
	{
		await ExecuteWithRetryAsync(
			"MATCH (d:SysManagedAttribute {packageId: $id, objid: $objid, attribute: $attribute}) DETACH DELETE d",
			new { id = packageId, objid, attribute });
	}

	public async Task UpsertManagedStructureAsync(ManagedStructureRecord record)
	{
		await ExecuteWithRetryAsync("""
			MERGE (d:SysManagedStructure {packageId: $packageId, objid: $objid})
			SET d.structureJson = $structureJson, d.baselineVersion = $baselineVersion
			""",
			new
			{
				packageId = record.PackageId,
				objid = record.Objid,
				structureJson = record.StructureJson,
				baselineVersion = record.BaselineVersion
			});
	}

	public async Task<IReadOnlyList<ManagedStructureRecord>> GetManagedStructuresAsync(string packageId)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (d:SysManagedStructure {packageId: $id}) RETURN d ORDER BY d.objid",
			new { id = packageId });

		return result.Result.Select(r =>
		{
			var node = r["d"].As<INode>();
			return new ManagedStructureRecord(
				node.Properties["packageId"].As<string>(),
				node.Properties["objid"].As<string>(),
				node.Properties["structureJson"].As<string>(),
				node.Properties["baselineVersion"].As<string>());
		}).ToList();
	}

	public async Task RemoveManagedStructureAsync(string packageId, string objid)
	{
		await ExecuteWithRetryAsync(
			"MATCH (d:SysManagedStructure {packageId: $id, objid: $objid}) DETACH DELETE d",
			new { id = packageId, objid });
	}

	public async Task SetPackageDependenciesAsync(string packageId, IReadOnlyList<PackageDependencyRecord> dependencies)
	{
		await ExecuteWithRetryAsync(
			"MATCH (:SysPackage {id: $id})-[e:DEPENDS_ON]->() DELETE e", new { id = packageId });

		foreach (var dependency in dependencies)
		{
			await ExecuteWithRetryAsync("""
				MATCH (a:SysPackage {id: $from}), (b:SysPackage {id: $to})
				MERGE (a)-[e:DEPENDS_ON]->(b)
				SET e.constraint = $constraint
				""",
				new { from = packageId, to = dependency.DependsOnId, constraint = dependency.Constraint });
		}
	}

	public async Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependenciesAsync(string packageId)
	{
		var result = await ExecuteWithRetryAsync("""
			MATCH (a:SysPackage {id: $id})-[e:DEPENDS_ON]->(b:SysPackage)
			RETURN a.id AS fromId, b.id AS toId, e.constraint AS constraint
			""", new { id = packageId });

		return result.Result.Select(MapDependency).ToList();
	}

	public async Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependentsAsync(string packageId)
	{
		var result = await ExecuteWithRetryAsync("""
			MATCH (a:SysPackage)-[e:DEPENDS_ON]->(b:SysPackage {id: $id})
			RETURN a.id AS fromId, b.id AS toId, e.constraint AS constraint
			""", new { id = packageId });

		return result.Result.Select(MapDependency).ToList();
	}

	private static PackageDependencyRecord MapDependency(IRecord record) => new(
		record["fromId"].As<string>(),
		record["toId"].As<string>(),
		record["constraint"].As<string>());

	public async Task UpsertPackageRemoteAsync(PackageRemoteRecord remote)
	{
		await ExecuteWithRetryAsync("""
			MERGE (d:SysRemote {name: $name})
			SET d.url = $url, d.trust = $trust, d.branch = $branch
			""",
			new
			{
				name = remote.Name,
				url = remote.Url,
				trust = remote.Trust.ToString().ToLowerInvariant(),
				branch = remote.Branch
			});
	}

	public async Task<IReadOnlyList<PackageRemoteRecord>> GetPackageRemotesAsync()
	{
		var result = await ExecuteWithRetryAsync("MATCH (d:SysRemote) RETURN d ORDER BY d.name");
		return result.Result.Select(r => MapRemoteNode(r["d"].As<INode>())).ToList();
	}

	public async Task<OneOf<PackageRemoteRecord, NotFound>> GetPackageRemoteAsync(string name)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (d:SysRemote {name: $name}) RETURN d", new { name });

		return result.Result.Count == 0
			? new NotFound()
			: MapRemoteNode(result.Result[0]["d"].As<INode>());
	}

	private static PackageRemoteRecord MapRemoteNode(INode node) => new(
		node.Properties["name"].As<string>(),
		node.Properties["url"].As<string>(),
		Enum.Parse<PackageRemoteTrust>(node.Properties["trust"].As<string>(), ignoreCase: true),
		OptionalString(node, "branch"));

	public async Task RemovePackageRemoteAsync(string name)
	{
		await ExecuteWithRetryAsync("MATCH (d:SysRemote {name: $name}) DETACH DELETE d", new { name });
	}

	public async Task AddPackageRevisionAsync(PackageRevisionRecord revision)
	{
		await ExecuteWithRetryAsync("""
			CREATE (d:SysPackageRevision {
				packageId: $packageId, revision: $revision, kind: $kind, version: $version, commit: $commit,
				manifestSnapshotJson: $manifestSnapshotJson, configureAnswersJson: $configureAnswersJson,
				preApplyValuesJson: $preApplyValuesJson, appliedAt: $appliedAt
			})
			""",
			new
			{
				packageId = revision.PackageId,
				revision = revision.Revision,
				kind = revision.Kind.ToString().ToLowerInvariant(),
				version = revision.Version,
				commit = revision.Commit,
				manifestSnapshotJson = revision.ManifestSnapshotJson,
				configureAnswersJson = revision.ConfigureAnswersJson,
				preApplyValuesJson = revision.PreApplyValuesJson,
				appliedAt = revision.AppliedAt.ToString("o", CultureInfo.InvariantCulture)
			});
	}

	public async Task<IReadOnlyList<PackageRevisionRecord>> GetPackageRevisionsAsync(string packageId)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (d:SysPackageRevision {packageId: $id}) RETURN d ORDER BY d.revision DESC",
			new { id = packageId });

		return result.Result.Select(r => MapRevisionNode(r["d"].As<INode>())).ToList();
	}

	public async Task<OneOf<PackageRevisionRecord, NotFound>> GetPackageRevisionAsync(string packageId, int revision)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (d:SysPackageRevision {packageId: $id, revision: $rev}) RETURN d",
			new { id = packageId, rev = revision });

		return result.Result.Count == 0
			? new NotFound()
			: MapRevisionNode(result.Result[0]["d"].As<INode>());
	}

	private static PackageRevisionRecord MapRevisionNode(INode node) => new(
		node.Properties["packageId"].As<string>(),
		node.Properties["revision"].As<int>(),
		Enum.Parse<PackageRevisionKind>(node.Properties["kind"].As<string>(), ignoreCase: true),
		node.Properties["version"].As<string>(),
		node.Properties["commit"].As<string>(),
		node.Properties["manifestSnapshotJson"].As<string>(),
		node.Properties["configureAnswersJson"].As<string>(),
		node.Properties["preApplyValuesJson"].As<string>(),
		ParsePackageTimestamp(node.Properties["appliedAt"].As<string>()));

	public async Task PrunePackageRevisionsAsync(string packageId, int keep)
	{
		await ExecuteWithRetryAsync("""
			MATCH (d:SysPackageRevision {packageId: $id})
			WITH d ORDER BY d.revision DESC
			SKIP $keep
			DETACH DELETE d
			""",
			new { id = packageId, keep });
	}

	#endregion
}
