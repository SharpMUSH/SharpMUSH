using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the portal role system (Discord-style RBAC): <c>sys_roles</c> document collection,
/// <c>edge_account_has_role</c> edge collection, and <c>graph_roles</c> named graph linking
/// accounts to roles. Roles are keyed by slug. Not visible to softcode; travels with backups.
/// </summary>
public class Migration_AddRoles : IArangoMigration
{
	public long Id => 20260614_001;

	public string Name => "add_roles";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.Roles))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.Roles,
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
							Name = new { type = DatabaseConstants.TypeString },
							Priority = new { type = DatabaseConstants.TypeNumber },
							IsSystem = new { type = DatabaseConstants.TypeBoolean }
						},
						required = (string[])["Slug", "Name"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.Roles, new ArangoIndex
			{
				Fields = ["Slug"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});
		}

		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.AccountHasRole))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.AccountHasRole,
				Type = ArangoCollectionType.Edge,
				WaitForSync = true
			});
		}

		var graphs = await migrator.Context.Graph.ListAsync(handle);
		if (!graphs.Any(g => g.Name == DatabaseConstants.GraphRoles))
		{
			await migrator.Context.Graph.CreateAsync(handle, new ArangoGraph
			{
				Name = DatabaseConstants.GraphRoles,
				EdgeDefinitions =
				[
					new ArangoEdgeDefinition
					{
						Collection = DatabaseConstants.AccountHasRole,
						From = [DatabaseConstants.Accounts],
						To = [DatabaseConstants.Roles]
					}
				]
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
