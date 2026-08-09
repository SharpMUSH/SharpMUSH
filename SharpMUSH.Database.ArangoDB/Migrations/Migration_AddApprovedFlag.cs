using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the engine-level <c>APPROVED</c> player flag: "this character has cleared whatever bar this
/// game sets for full participation". The engine ships the flag and the predicate that reads it
/// (<c>HelperFunctions.IsApproved</c> / the <c>isapproved()</c> function) and deliberately ships NO
/// policy for what earns it — a game decides that and sets the flag however it likes.
///
/// <para>Royalty-settable rather than wizard-settable so staff below wizard can run an approval
/// queue, matching the other staff-managed player flags (JUDGE, UNREGISTERED).</para>
///
/// <para>Runs on fresh and existing databases alike — an UPSERT keyed on the flag name, so a
/// database that somehow already carries an APPROVED flag is updated rather than duplicated. The
/// Memgraph and SurrealDB providers reach the same end through their always-run idempotent flag
/// seeds; only Arango needs a migration id.</para>
/// </summary>
public class Migration_AddApprovedFlag : IArangoMigration
{
	public long Id => 20260808_001;

	public string Name => "add_approved_flag";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle) =>
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"UPSERT { Name: @name } INSERT @doc UPDATE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ObjectFlags },
				{ "name", "APPROVED" },
				{
					"doc", new
					{
						Name = "APPROVED",
						Aliases = (string[])[],
						Symbol = "+",
						System = true,
						SetPermissions = DatabaseConstants.permissionsRoyalty,
						UnsetPermissions = DatabaseConstants.permissionsRoyalty,
						TypeRestrictions = DatabaseConstants.typesPlayer
					}
				}
			});

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
