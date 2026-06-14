using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds <c>sys_managed_structures</c> (decision 20.13, extended for full object
/// structure diffs): one JSON baseline document per (package, objid) holding the
/// object flags, powers, locks, and per-attribute flags a package set at its
/// last install/upgrade. Enables the three-way merge of object structure on
/// upgrade and exact rollback.
/// </summary>
public class Migration_AddManagedStructures : IArangoMigration
{
	public long Id => 20260613_002;

	public string Name => "add_managed_structures";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.ManagedStructures))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.ManagedStructures,
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
							StructureJson = new { type = DatabaseConstants.TypeString },
							BaselineVersion = new { type = DatabaseConstants.TypeString }
						},
						required = (string[])["PackageId", "Objid", "StructureJson"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.ManagedStructures, new ArangoIndex
			{
				Fields = ["PackageId", "Objid"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.ManagedStructures, new ArangoIndex
			{
				Fields = ["Objid"],
				Type = ArangoIndexType.Persistent
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
