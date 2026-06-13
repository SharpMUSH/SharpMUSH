using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the Dynamic Application registry collection (Area 21):
/// <c>sys_applications</c> — schema-driven pages and widgets an admin has linked
/// into the portal, keyed by slug. Not visible to softcode; travels with backups.
/// </summary>
public class Migration_AddApplications : IArangoMigration
{
	public long Id => 20260613_001;

	public string Name => "add_applications";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.Applications))
		{
			return;
		}

		await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
		{
			Name = DatabaseConstants.Applications,
			Type = ArangoCollectionType.Document,
			WaitForSync = true,
			Schema = new ArangoSchema
			{
				Rule = new
				{
					type = DatabaseConstants.TypeObject,
					properties = new
					{
						Slug = new { type = DatabaseConstants.TypeString },
						DisplayName = new { type = DatabaseConstants.TypeString },
						Kind = new { type = DatabaseConstants.TypeString },
						SchemaUrl = new { type = DatabaseConstants.TypeString },
						MinimumRole = new { type = DatabaseConstants.TypeString },
						Order = new { type = DatabaseConstants.TypeNumber }
					},
					required = (string[])["Slug", "DisplayName", "Kind", "SchemaUrl"],
					additionalProperties = true
				}
			}
		});

		await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.Applications, new ArangoIndex
		{
			Fields = ["Slug"],
			Unique = true,
			Type = ArangoIndexType.Persistent
		});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
