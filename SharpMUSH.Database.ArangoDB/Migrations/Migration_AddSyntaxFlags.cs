using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the <c>cmdsyntax</c> and <c>funsyntax</c> attribute flags, which declare which
/// softcode dialect an attribute holds so display commands can format it correctly.
///
/// <para>An attribute that does not begin with <c>$</c> is genuinely ambiguous between a
/// command list invoked by <c>@trigger</c> and a function body invoked by <c>u()</c>. Both
/// parse, differently. These flags remove the guess; they map onto <c>ParseType.CommandList</c>
/// and <c>ParseType.Function</c> respectively.</para>
///
/// <para>UPSERT keyed on name, so it runs on fresh and existing databases alike. The Memgraph
/// and SurrealDB providers reach the same end through their always-run idempotent flag seeds;
/// only Arango needs a migration id.</para>
/// </summary>
public class Migration_AddSyntaxFlags : IArangoMigration
{
	public long Id => 20260823_001;

	public string Name => "add_syntax_flags";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		foreach (var (name, symbol) in new[] { ("cmdsyntax", "x"), ("funsyntax", "f") })
		{
			await migrator.Context.Query.ExecuteAsync<object>(
				handle,
				"UPSERT { Name: @name } INSERT @doc UPDATE @doc IN @@c",
				bindVars: new Dictionary<string, object>
				{
					{ "@c", DatabaseConstants.AttributeFlags },
					{ "name", name },
					{ "doc", new { Name = name, Symbol = symbol, System = true, Inheritable = true } }
				});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
