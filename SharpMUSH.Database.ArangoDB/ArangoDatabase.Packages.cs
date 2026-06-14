using System.Globalization;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase : IPackageRegistryService
{
	#region Package Registry

	private class PackageDbDoc
	{
		public string PackageId { get; set; } = "";
		public string Version { get; set; } = "";
		public string SourceRepo { get; set; } = "";
		public string? SourcePath { get; set; }
		public string InstalledCommit { get; set; } = "";
		public string? PinnedBranch { get; set; }
		public string InstalledAt { get; set; } = "";
		public int CurrentRevision { get; set; }
	}

	private class PackageObjectDbDoc
	{
		public string PackageId { get; set; } = "";
		public string Ref { get; set; } = "";
		public string Objid { get; set; } = "";
		public string Type { get; set; } = "";
	}

	private class ManagedAttributeDbDoc
	{
		public string PackageId { get; set; } = "";
		public string Objid { get; set; } = "";
		public string Attribute { get; set; } = "";
		public string BaselineValue { get; set; } = "";
		public string BaselineHash { get; set; } = "";
		public string BaselineVersion { get; set; } = "";
	}

	private class ManagedStructureDbDoc
	{
		public string PackageId { get; set; } = "";
		public string Objid { get; set; } = "";
		public string StructureJson { get; set; } = "";
		public string BaselineVersion { get; set; } = "";
	}

	private class PackageDependencyDbDoc
	{
		public string PackageId { get; set; } = "";
		public string DependsOnId { get; set; } = "";
		public string Constraint { get; set; } = "";
	}

	private class PackageRemoteDbDoc
	{
		public string Name { get; set; } = "";
		public string Url { get; set; } = "";
		public string Trust { get; set; } = "";
		public string? Branch { get; set; }
	}

	private class PackageRevisionDbDoc
	{
		public string PackageId { get; set; } = "";
		public int RevisionNumber { get; set; }
		public string Kind { get; set; } = "";
		public string Version { get; set; } = "";
		public string Commit { get; set; } = "";
		public string ManifestSnapshotJson { get; set; } = "";
		public string ConfigureAnswersJson { get; set; } = "";
		public string PreApplyValuesJson { get; set; } = "";
		public string AppliedAt { get; set; } = "";
	}

	private static DateTimeOffset ParseTimestamp(string iso) =>
		DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

	// ── Installed packages ─────────────────────────────────────────────────

	public async Task UpsertInstalledPackageAsync(InstalledPackageRecord package)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @insertDoc REPLACE @replaceDoc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Packages },
				{ "key", package.Id },
				{ "insertDoc", ToDoc(package, withKey: true) },
				{ "replaceDoc", ToDoc(package, withKey: true) }
			});

		static Dictionary<string, object?> ToDoc(InstalledPackageRecord p, bool withKey)
		{
			var doc = new Dictionary<string, object?>
			{
				["PackageId"] = p.Id,
				["Version"] = p.Version,
				["SourceRepo"] = p.SourceRepo,
				["SourcePath"] = p.SourcePath,
				["InstalledCommit"] = p.InstalledCommit,
				["PinnedBranch"] = p.PinnedBranch,
				["InstalledAt"] = p.InstalledAt.ToString("o", CultureInfo.InvariantCulture),
				["CurrentRevision"] = p.CurrentRevision
			};
			if (withKey)
			{
				doc["_key"] = p.Id;
			}

			return doc;
		}
	}

	public async Task<OneOf<InstalledPackageRecord, NotFound>> GetInstalledPackageAsync(string packageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageDbDoc>(handle,
			"FOR p IN @@c FILTER p._key == @key RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Packages },
				{ "key", packageId }
			});

		return result.Count == 0 ? new NotFound() : MapPackage(result[0]);
	}

	public async Task<IReadOnlyList<InstalledPackageRecord>> GetInstalledPackagesAsync()
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageDbDoc>(handle,
			"FOR p IN @@c SORT p.PackageId RETURN p",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Packages } });

		return result.Select(MapPackage).ToList();
	}

	private static InstalledPackageRecord MapPackage(PackageDbDoc doc) => new(
		doc.PackageId, doc.Version, doc.SourceRepo, doc.SourcePath, doc.InstalledCommit,
		doc.PinnedBranch, ParseTimestamp(doc.InstalledAt), doc.CurrentRevision);

	public async Task RemoveInstalledPackageAsync(string packageId)
	{
		foreach (var collection in (string[])
			[DatabaseConstants.PackageObjects, DatabaseConstants.ManagedAttributes,
				DatabaseConstants.ManagedStructures, DatabaseConstants.PackageRevisions])
		{
			await arangoDb.Query.ExecuteAsync<object>(handle,
				"FOR d IN @@c FILTER d.PackageId == @id REMOVE d IN @@c",
				bindVars: new Dictionary<string, object> { { "@c", collection }, { "id", packageId } });
		}

		var packageDocId = $"{DatabaseConstants.Packages}/{packageId}";
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR e IN @@c FILTER e._from == @docId OR e._to == @docId REMOVE e IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageDependsOn },
				{ "docId", packageDocId }
			});

		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR p IN @@c FILTER p._key == @key REMOVE p IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Packages },
				{ "key", packageId }
			});
	}

	// ── Package-created objects ────────────────────────────────────────────

	public async Task UpsertPackageObjectAsync(PackageObjectRecord record)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { PackageId: @pkg, Ref: @ref } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageObjects },
				{ "pkg", record.PackageId },
				{ "ref", record.Ref },
				{
					"doc", new Dictionary<string, object>
					{
						["PackageId"] = record.PackageId,
						["Ref"] = record.Ref,
						["Objid"] = record.Objid,
						["Type"] = record.Type
					}
				}
			});
	}

	public async Task<IReadOnlyList<PackageObjectRecord>> GetPackageObjectsAsync(string packageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageObjectDbDoc>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id SORT d.Ref RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageObjects },
				{ "id", packageId }
			});

		return result.Select(d => new PackageObjectRecord(d.PackageId, d.Ref, d.Objid, d.Type)).ToList();
	}

	public async Task RemovePackageObjectAsync(string packageId, string @ref)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id AND d.Ref == @ref REMOVE d IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageObjects },
				{ "id", packageId },
				{ "ref", @ref }
			});
	}

	// ── Managed attributes ─────────────────────────────────────────────────

	public async Task UpsertManagedAttributeAsync(ManagedAttributeRecord record)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { PackageId: @pkg, Objid: @objid, Attribute: @attr } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ManagedAttributes },
				{ "pkg", record.PackageId },
				{ "objid", record.Objid },
				{ "attr", record.Attribute },
				{
					"doc", new Dictionary<string, object>
					{
						["PackageId"] = record.PackageId,
						["Objid"] = record.Objid,
						["Attribute"] = record.Attribute,
						["BaselineValue"] = record.BaselineValue,
						["BaselineHash"] = record.BaselineHash,
						["BaselineVersion"] = record.BaselineVersion
					}
				}
			});
	}

	public async Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesAsync(string packageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<ManagedAttributeDbDoc>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id SORT d.Objid, d.Attribute RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ManagedAttributes },
				{ "id", packageId }
			});

		return result.Select(MapManagedAttribute).ToList();
	}

	public async Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesForObjectAsync(string objid)
	{
		var result = await arangoDb.Query.ExecuteAsync<ManagedAttributeDbDoc>(handle,
			"FOR d IN @@c FILTER d.Objid == @objid SORT d.Attribute RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ManagedAttributes },
				{ "objid", objid }
			});

		return result.Select(MapManagedAttribute).ToList();
	}

	private static ManagedAttributeRecord MapManagedAttribute(ManagedAttributeDbDoc d) => new(
		d.PackageId, d.Objid, d.Attribute, d.BaselineValue, d.BaselineHash, d.BaselineVersion);

	public async Task RemoveManagedAttributeAsync(string packageId, string objid, string attribute)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id AND d.Objid == @objid AND d.Attribute == @attr REMOVE d IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ManagedAttributes },
				{ "id", packageId },
				{ "objid", objid },
				{ "attr", attribute }
			});
	}

	// ── Managed object structure ────────────────────────────────────────────

	public async Task UpsertManagedStructureAsync(ManagedStructureRecord record)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { PackageId: @pkg, Objid: @objid } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ManagedStructures },
				{ "pkg", record.PackageId },
				{ "objid", record.Objid },
				{
					"doc", new Dictionary<string, object>
					{
						["PackageId"] = record.PackageId,
						["Objid"] = record.Objid,
						["StructureJson"] = record.StructureJson,
						["BaselineVersion"] = record.BaselineVersion
					}
				}
			});
	}

	public async Task<IReadOnlyList<ManagedStructureRecord>> GetManagedStructuresAsync(string packageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<ManagedStructureDbDoc>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id SORT d.Objid RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ManagedStructures },
				{ "id", packageId }
			});

		return result
			.Select(d => new ManagedStructureRecord(d.PackageId, d.Objid, d.StructureJson, d.BaselineVersion))
			.ToList();
	}

	public async Task RemoveManagedStructureAsync(string packageId, string objid)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id AND d.Objid == @objid REMOVE d IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ManagedStructures },
				{ "id", packageId },
				{ "objid", objid }
			});
	}

	// ── Dependencies (edge collection) ─────────────────────────────────────

	public async Task SetPackageDependenciesAsync(string packageId, IReadOnlyList<PackageDependencyRecord> dependencies)
	{
		var fromDocId = $"{DatabaseConstants.Packages}/{packageId}";
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR e IN @@c FILTER e._from == @from REMOVE e IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageDependsOn },
				{ "from", fromDocId }
			});

		if (dependencies.Count == 0)
		{
			return;
		}

		var edges = dependencies.Select(d => new Dictionary<string, object>
		{
			["_from"] = fromDocId,
			["_to"] = $"{DatabaseConstants.Packages}/{d.DependsOnId}",
			["Constraint"] = d.Constraint
		}).ToList();

		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR e IN @edges INSERT e IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageDependsOn },
				{ "edges", edges }
			});
	}

	public async Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependenciesAsync(string packageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageDependencyDbDoc>(handle,
			"""
			FOR e IN @@c FILTER e._from == @docId
				RETURN { PackageId: PARSE_IDENTIFIER(e._from).key, DependsOnId: PARSE_IDENTIFIER(e._to).key, Constraint: e.Constraint }
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageDependsOn },
				{ "docId", $"{DatabaseConstants.Packages}/{packageId}" }
			});

		return result.Select(d => new PackageDependencyRecord(d.PackageId, d.DependsOnId, d.Constraint)).ToList();
	}

	public async Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependentsAsync(string packageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageDependencyDbDoc>(handle,
			"""
			FOR e IN @@c FILTER e._to == @docId
				RETURN { PackageId: PARSE_IDENTIFIER(e._from).key, DependsOnId: PARSE_IDENTIFIER(e._to).key, Constraint: e.Constraint }
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageDependsOn },
				{ "docId", $"{DatabaseConstants.Packages}/{packageId}" }
			});

		return result.Select(d => new PackageDependencyRecord(d.PackageId, d.DependsOnId, d.Constraint)).ToList();
	}

	// ── Remotes ────────────────────────────────────────────────────────────

	public async Task UpsertPackageRemoteAsync(PackageRemoteRecord remote)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { Name: @name } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageRemotes },
				{ "name", remote.Name },
				{
					"doc", new Dictionary<string, object?>
					{
						["Name"] = remote.Name,
						["Url"] = remote.Url,
						["Trust"] = remote.Trust.ToString().ToLowerInvariant(),
						["Branch"] = remote.Branch
					}
				}
			});
	}

	public async Task<IReadOnlyList<PackageRemoteRecord>> GetPackageRemotesAsync()
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageRemoteDbDoc>(handle,
			"FOR d IN @@c SORT d.Name RETURN d",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.PackageRemotes } });

		return result.Select(MapRemote).ToList();
	}

	public async Task<OneOf<PackageRemoteRecord, NotFound>> GetPackageRemoteAsync(string name)
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageRemoteDbDoc>(handle,
			"FOR d IN @@c FILTER d.Name == @name RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageRemotes },
				{ "name", name }
			});

		return result.Count == 0 ? new NotFound() : MapRemote(result[0]);
	}

	private static PackageRemoteRecord MapRemote(PackageRemoteDbDoc d) => new(
		d.Name, d.Url, Enum.Parse<PackageRemoteTrust>(d.Trust, ignoreCase: true), d.Branch);

	public async Task RemovePackageRemoteAsync(string name)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.Name == @name REMOVE d IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageRemotes },
				{ "name", name }
			});
	}

	// ── Revisions ──────────────────────────────────────────────────────────

	public async Task AddPackageRevisionAsync(PackageRevisionRecord revision)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"INSERT @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageRevisions },
				{
					"doc", new Dictionary<string, object>
					{
						["PackageId"] = revision.PackageId,
						["RevisionNumber"] = revision.Revision,
						["Kind"] = revision.Kind.ToString().ToLowerInvariant(),
						["Version"] = revision.Version,
						["Commit"] = revision.Commit,
						["ManifestSnapshotJson"] = revision.ManifestSnapshotJson,
						["ConfigureAnswersJson"] = revision.ConfigureAnswersJson,
						["PreApplyValuesJson"] = revision.PreApplyValuesJson,
						["AppliedAt"] = revision.AppliedAt.ToString("o", CultureInfo.InvariantCulture)
					}
				}
			});
	}

	public async Task<IReadOnlyList<PackageRevisionRecord>> GetPackageRevisionsAsync(string packageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageRevisionDbDoc>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id SORT d.RevisionNumber DESC RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageRevisions },
				{ "id", packageId }
			});

		return result.Select(MapRevision).ToList();
	}

	public async Task<OneOf<PackageRevisionRecord, NotFound>> GetPackageRevisionAsync(string packageId, int revision)
	{
		var result = await arangoDb.Query.ExecuteAsync<PackageRevisionDbDoc>(handle,
			"FOR d IN @@c FILTER d.PackageId == @id AND d.RevisionNumber == @rev RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageRevisions },
				{ "id", packageId },
				{ "rev", revision }
			});

		return result.Count == 0 ? new NotFound() : MapRevision(result[0]);
	}

	private static PackageRevisionRecord MapRevision(PackageRevisionDbDoc d) => new(
		d.PackageId, d.RevisionNumber, Enum.Parse<PackageRevisionKind>(d.Kind, ignoreCase: true),
		d.Version, d.Commit, d.ManifestSnapshotJson, d.ConfigureAnswersJson, d.PreApplyValuesJson,
		ParseTimestamp(d.AppliedAt));

	public async Task PrunePackageRevisionsAsync(string packageId, int keep)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"""
			FOR d IN @@c FILTER d.PackageId == @id
				SORT d.RevisionNumber DESC
				LIMIT @keep, 1000000
				REMOVE d IN @@c
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.PackageRevisions },
				{ "id", packageId },
				{ "keep", keep }
			});
	}

	#endregion
}
