using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the sys_server_state single-document collection and infers SetupCompleted
/// for existing deployments: a game that already has an account with a non-empty
/// password hash has been claimed and must not re-open the first-run wizard.
/// </summary>
public class Migration_AddServerState : IArangoMigration
{
	public long Id => 20260713_001; // highest existing is 20260614_002

	public string Name => "add_server_state";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.ServerState))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.ServerState,
				Type = ArangoCollectionType.Document
			});
		}

		// Upgrade inference: any claimed account (non-empty PasswordHash) => setup done.
		var claimed = await migrator.Context.Query.ExecuteAsync<bool>(handle,
			"FOR a IN @@c FILTER a.PasswordHash != null AND a.PasswordHash != '' LIMIT 1 RETURN true",
			new Dictionary<string, object> { { "@c", DatabaseConstants.Accounts } });

		// INSERT-or-keep: never downgrade an existing flag if the migration re-runs.
		await migrator.Context.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc UPDATE {} IN @@c",
			new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ServerState },
				{ "key", "state" },
				{ "doc", new Dictionary<string, object?> { ["_key"] = "state", ["SetupCompleted"] = claimed.FirstOrDefault() } }
			});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
