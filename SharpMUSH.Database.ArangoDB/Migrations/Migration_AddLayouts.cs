using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the admin-customized layout collection: <c>sys_layouts</c> — one
/// <c>LayoutConfiguration</c> JSON blob per named scope (e.g. "global", "home",
/// "wiki-index", "profile"), keyed by scope. Not visible to softcode; travels with backups.
/// </summary>
public class Migration_AddLayouts : IArangoMigration
{
	public long Id => 20260614_002;

	public string Name => "add_layouts";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.Layouts))
		{
			return;
		}

		await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
		{
			Name = DatabaseConstants.Layouts,
			Type = ArangoCollectionType.Document,
			WaitForSync = true,
			Schema = new ArangoSchema
			{
				Rule = new
				{
					type = DatabaseConstants.TypeObject,
					properties = new
					{
						Scope = new { type = DatabaseConstants.TypeString },
						Json = new { type = DatabaseConstants.TypeString }
					},
					required = (string[])["Scope", "Json"],
					additionalProperties = true
				}
			}
		});

		await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.Layouts, new ArangoIndex
		{
			Fields = ["Scope"],
			Unique = true,
			Type = ArangoIndexType.Persistent
		});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
