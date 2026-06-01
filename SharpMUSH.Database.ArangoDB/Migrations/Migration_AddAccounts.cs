using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the account system: <c>node_accounts</c> vertex collection,
/// <c>edge_account_owns_character</c> edge collection, and <c>graph_accounts</c> named graph.
/// </summary>
public class Migration_AddAccounts : IArangoMigration
{
	public long Id => 20250101_001;

	public string Name => "add_accounts";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		await migrator.ApplyStructureAsync(handle, new ArangoStructure()
		{
			Collections =
			[
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.Accounts,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new
							{
								type = DatabaseConstants.TypeObject,
								properties = new
								{
									DisplayName = new { type = DatabaseConstants.TypeString },
									PasswordHash = new { type = DatabaseConstants.TypeString },
									CreatedAt = new { type = DatabaseConstants.TypeNumber },
									UpdatedAt = new { type = DatabaseConstants.TypeNumber },
									IsVerified = new { type = DatabaseConstants.TypeBoolean },
									IsDisabled = new { type = DatabaseConstants.TypeBoolean }
								},
								required = (string[])["DisplayName", "PasswordHash"],
								additionalProperties = true
							}
						}
					},
					Indices =
					[
						new()
						{
							Fields = ["DisplayName"],
							Unique = true
						},
						new()
						{
							Fields = ["Email"],
							Unique = true,
							Sparse = true
						}
					]
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.AccountOwnsCharacter,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				}
			],
			Graphs =
			[
				new ArangoGraph
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
				}
			]
		});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
