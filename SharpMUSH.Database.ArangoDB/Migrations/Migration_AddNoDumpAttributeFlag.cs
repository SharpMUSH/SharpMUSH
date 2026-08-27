using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the <c>nodump</c> attribute flag (PennMUSH's <c>AF_NODUMP</c>, <c>hdrs/attrib.h:153</c>).
///
/// <para>Like <c>AF_INTERNAL</c>, Penn's own attribute-privilege tables give this flag no
/// user-settable letter - it is absent from BOTH <c>attr_privs_set</c> and
/// <c>attr_privs_view</c> (<c>src/atr_tab.c:34-90</c>) entirely, since a real PennMUSH player
/// can never toggle it: it exists purely on hardcoded system attributes (semaphores) and is
/// enforced only by <c>can_create_attr</c>'s <c>player != GOD</c> guard
/// (<c>src/attrib.c:479-483</c>). Seeding a real symbol here (previously <c>"D"</c>) let a bare
/// <c>@set obj/attr=D</c> collide with it, so this seeds an empty <c>Symbol</c> instead -
/// unreachable via any single-character token, matching that it has no settable letter in Penn
/// at all.</para>
///
/// <para>UPSERT keyed on name, so it runs on fresh and existing databases alike. The Memgraph
/// and SurrealDB providers reach the same end through their always-run idempotent flag seeds;
/// only Arango needs a migration id.</para>
/// </summary>
public class Migration_AddNoDumpAttributeFlag : IArangoMigration
{
	public long Id => 20260824_002;

	public string Name => "add_nodump_attribute_flag";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"UPSERT { Name: @name } INSERT @doc UPDATE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.AttributeFlags },
				{ "name", "nodump" },
				{ "doc", new { Name = "nodump", Symbol = "", System = true, Inheritable = true } }
			});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
