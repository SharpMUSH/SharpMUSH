using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Gives every power document a <c>Symbol</c>, the one-character abbreviation <c>@power/letter</c>
/// writes. <see cref="Migration_CreateDatabase"/> declares <c>Symbol</c> in the <c>ObjectPowers</c>
/// schema rule but its seeds omit the property, matching PennMUSH, where every entry in
/// <c>hdrs/flag_tab.h</c> <c>power_table</c> has <c>letter</c> <c>'\0'</c>.
///
/// <para>Absent and empty are not the same to AQL — <c>FILTER p.Symbol == ""</c> does not match a
/// document with no <c>Symbol</c> — so <c>@power/letter</c>'s collision scan would read seeded and
/// user-created powers differently. Writing the empty string makes them indistinguishable.</para>
///
/// <para>Idempotent: only documents whose <c>Symbol</c> is null or missing are touched, so an
/// assigned letter survives a re-run.</para>
/// </summary>
public class Migration_AddPowerSymbol : IArangoMigration
{
	public long Id => 20260815_001;

	public string Name => "add_power_symbol";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle) =>
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"""
			FOR power IN @@c
				FILTER power.Symbol == null
				UPDATE power WITH { Symbol: "" } IN @@c
			""",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.ObjectPowers } });

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
