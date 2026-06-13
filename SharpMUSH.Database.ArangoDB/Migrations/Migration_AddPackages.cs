using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the softcode package manager system collections (decisions 20.3 and
/// 20.13): <c>sys_packages</c>, <c>sys_package_objects</c>,
/// <c>sys_package_depends</c> (edge), <c>sys_managed_attributes</c> (full
/// baseline values), <c>sys_remotes</c>, and <c>sys_package_revisions</c>.
/// </summary>
public class Migration_AddPackages : IArangoMigration
{
	public long Id => 20260612_001;

	public string Name => "add_packages";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		// ── sys_packages ──────────────────────────────────────────────────────
		// _key is the package id, so dependency edges can target sys_packages/<id>.
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.Packages))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.Packages,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							PackageId = new { type = DatabaseConstants.TypeString },
							Version = new { type = DatabaseConstants.TypeString },
							SourceRepo = new { type = DatabaseConstants.TypeString },
							InstalledCommit = new { type = DatabaseConstants.TypeString },
							InstalledAt = new { type = DatabaseConstants.TypeString },
							CurrentRevision = new { type = DatabaseConstants.TypeNumber }
						},
						required = (string[])["PackageId", "Version", "SourceRepo", "InstalledCommit"],
						additionalProperties = true
					}
				}
			});
		}

		// ── sys_package_objects ───────────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.PackageObjects))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.PackageObjects,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							PackageId = new { type = DatabaseConstants.TypeString },
							Ref = new { type = DatabaseConstants.TypeString },
							Objid = new { type = DatabaseConstants.TypeString },
							Type = new { type = DatabaseConstants.TypeString }
						},
						required = (string[])["PackageId", "Ref", "Objid"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.PackageObjects, new ArangoIndex
			{
				Fields = ["PackageId", "Ref"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.PackageObjects, new ArangoIndex
			{
				Fields = ["Objid"],
				Type = ArangoIndexType.Persistent
			});
		}

		// ── sys_package_depends (edge: sys_packages -> sys_packages) ─────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.PackageDependsOn))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.PackageDependsOn,
				Type = ArangoCollectionType.Edge,
				WaitForSync = true
			});
		}

		// ── sys_managed_attributes ────────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.ManagedAttributes))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.ManagedAttributes,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							PackageId = new { type = DatabaseConstants.TypeString },
							Objid = new { type = DatabaseConstants.TypeString },
							Attribute = new { type = DatabaseConstants.TypeString },
							BaselineValue = new { type = DatabaseConstants.TypeString },
							BaselineHash = new { type = DatabaseConstants.TypeString },
							BaselineVersion = new { type = DatabaseConstants.TypeString }
						},
						required = (string[])["PackageId", "Objid", "Attribute", "BaselineValue", "BaselineHash"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.ManagedAttributes, new ArangoIndex
			{
				Fields = ["PackageId", "Objid", "Attribute"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.ManagedAttributes, new ArangoIndex
			{
				Fields = ["Objid"],
				Type = ArangoIndexType.Persistent
			});
		}

		// ── sys_remotes ───────────────────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.PackageRemotes))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.PackageRemotes,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							Name = new { type = DatabaseConstants.TypeString },
							Url = new { type = DatabaseConstants.TypeString },
							Trust = new { type = DatabaseConstants.TypeString }
						},
						required = (string[])["Name", "Url", "Trust"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.PackageRemotes, new ArangoIndex
			{
				Fields = ["Name"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});
		}

		// ── sys_package_revisions ─────────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.PackageRevisions))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.PackageRevisions,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							PackageId = new { type = DatabaseConstants.TypeString },
							RevisionNumber = new { type = DatabaseConstants.TypeNumber },
							Kind = new { type = DatabaseConstants.TypeString },
							Version = new { type = DatabaseConstants.TypeString },
							Commit = new { type = DatabaseConstants.TypeString },
							ManifestSnapshotJson = new { type = DatabaseConstants.TypeString },
							ConfigureAnswersJson = new { type = DatabaseConstants.TypeString },
							PreApplyValuesJson = new { type = DatabaseConstants.TypeString },
							AppliedAt = new { type = DatabaseConstants.TypeString }
						},
						required = (string[])["PackageId", "RevisionNumber", "Kind", "Version"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.PackageRevisions, new ArangoIndex
			{
				Fields = ["PackageId", "RevisionNumber"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
