using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the node_sessions collection backing persisted web account sessions
/// (one document per token, keyed by _key = token).
/// </summary>
public class Migration_AddSessions : IArangoMigration
{
	public long Id => 20260714_001;

	public string Name => "add_sessions";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.Sessions))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.Sessions,
				Type = ArangoCollectionType.Document
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
