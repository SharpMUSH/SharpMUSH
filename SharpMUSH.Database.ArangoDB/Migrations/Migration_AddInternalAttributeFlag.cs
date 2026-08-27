using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the <c>internal</c> attribute flag (PennMUSH's <c>AF_INTERNAL</c>, <c>src/atr_tab.c:75</c>).
///
/// <para>Penn's own attribute-privilege table gives this flag no user-settable letter (it is
/// present in <c>attr_privs_view</c> only, absent from <c>attr_privs_set</c>, with symbol
/// <c>'\0'</c>) — it exists purely for hardcoded, engine-internal attributes that no command
/// can ever flag or unflag. Seeding a real symbol here (previously <c>"I"</c>) let a bare
/// <c>@set obj/attr=I</c> collide with it, so this matches Penn's <c>'\0'</c> with an empty
/// <c>Symbol</c> instead — unreachable via any single-character token, exactly like Penn.</para>
///
/// <para>UPSERT keyed on name, so it runs on fresh and existing databases alike. The Memgraph
/// and SurrealDB providers reach the same end through their always-run idempotent flag seeds;
/// only Arango needs a migration id.</para>
/// </summary>
public class Migration_AddInternalAttributeFlag : IArangoMigration
{
	public long Id => 20260824_001;

	public string Name => "add_internal_attribute_flag";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"UPSERT { Name: @name } INSERT @doc UPDATE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.AttributeFlags },
				{ "name", "internal" },
				{ "doc", new { Name = "internal", Symbol = "", System = true, Inheritable = true } }
			});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
