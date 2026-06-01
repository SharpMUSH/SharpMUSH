using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the account system: <c>node_accounts</c> vertex collection,
/// <c>edge_account_owns_character</c> edge collection, and <c>graph_accounts</c> named graph.
/// Uses direct API calls instead of ApplyStructureAsync to avoid an ArangoDB 3.10+ JSON
/// deserialization bug in Core.Arango's index listing.
/// </summary>
public class Migration_AddAccounts : IArangoMigration
{
	public long Id => 20250101_001;

	public string Name => "add_accounts";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.Accounts))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.Accounts,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							Username = new { type = DatabaseConstants.TypeString },
							PasswordHash = new { type = DatabaseConstants.TypeString },
							CreatedAt = new { type = DatabaseConstants.TypeNumber },
							UpdatedAt = new { type = DatabaseConstants.TypeNumber },
							IsVerified = new { type = DatabaseConstants.TypeBoolean },
							IsDisabled = new { type = DatabaseConstants.TypeBoolean }
						},
						required = (string[])["Username", "PasswordHash"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.Accounts, new ArangoIndex
			{
				Fields = ["Username"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.Accounts, new ArangoIndex
			{
				Fields = ["Email"],
				Unique = true,
				Sparse = true,
				Type = ArangoIndexType.Persistent
			});
		}

		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.AccountOwnsCharacter))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.AccountOwnsCharacter,
				Type = ArangoCollectionType.Edge,
				WaitForSync = true
			});
		}

		var graphs = await migrator.Context.Graph.ListAsync(handle);
		if (!graphs.Any(g => g.Name == DatabaseConstants.GraphAccounts))
		{
			await migrator.Context.Graph.CreateAsync(handle, new ArangoGraph
			{
				Name = DatabaseConstants.GraphAccounts,
				EdgeDefinitions =
				[
					new ArangoEdgeDefinition
					{
						Collection = DatabaseConstants.AccountOwnsCharacter,
						From = [DatabaseConstants.Accounts],
						To = [DatabaseConstants.Players]
					}
				]
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
