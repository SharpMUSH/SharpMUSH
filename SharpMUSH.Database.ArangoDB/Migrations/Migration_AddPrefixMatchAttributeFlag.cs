using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the <c>prefixmatch</c> attribute flag, which ArangoDB has been missing since the initial
/// schema while both other providers seed it (<c>MemgraphDatabase.Migration.cs</c> and
/// <c>SurrealDatabase.Migration.cs</c>, <c>("prefixmatch", "", false)</c>).
///
/// <para>The gap is visible in Arango's own data: <c>Migration_CreateDatabase</c> names
/// <c>prefixmatch</c> in the <c>DefaultFlags</c> of 127 standard attribute entries, and
/// <c>SetAttributeAsync</c> resolves each of those names to a flag row and silently skips the ones
/// it cannot find. So on Arango every one of those 127 attributes is created without the flag its
/// own entry asks for.</para>
///
/// <para>Nothing reads it back today — <c>SharpAttributeExtensions.IsPrefixMatch</c> has no callers,
/// and the prefix-matching that <c>SharpMUSHParserVisitor</c> actually performs reads the attribute
/// ENTRY's <c>DefaultFlags</c> list rather than the resolved flag, so it works on Arango regardless.
/// This is a consistency fix that stops the three providers disagreeing about what flags exist, not
/// a behaviour change.</para>
///
/// <para>Symbol is deliberately the empty string, matching the other two providers: PennMUSH has no
/// character for this flag.</para>
///
/// <para>UPSERT keyed on the flag name, so a database that somehow already carries it is updated
/// rather than duplicated. Memgraph and SurrealDB reach the same end through their always-run
/// idempotent flag seeds; only Arango needs a migration id.</para>
/// </summary>
public class Migration_AddPrefixMatchAttributeFlag : IArangoMigration
{
	public long Id => 20260825_001;

	public string Name => "add_prefixmatch_attribute_flag";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle) =>
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"UPSERT { Name: @name } INSERT @doc UPDATE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.AttributeFlags },
				{ "name", "prefixmatch" },
				{ "doc", new { Name = "prefixmatch", Symbol = "", System = true, Inheritable = false } }
			});

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
