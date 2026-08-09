using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Splits MYOPIC back out of MISTRUST, and removes the duplicate MISTRUST row that carried it.
///
/// <para>PennMUSH has two flags on the letter <c>m</c>, told apart by object type: MISTRUST
/// (<c>src/flags.c:778</c> — <c>TYPE_THING | TYPE_EXIT | TYPE_ROOM</c>, which stops an object
/// controlling others via Control/Zone locks or same-owner control) and MYOPIC
/// (<c>hdrs/flag_tab.h:51</c> — <c>TYPE_PLAYER</c>, which hides dbrefs and flag lists after the names
/// of objects you control). <c>game/txt/hlp/pennflag.hlp:37</c> prints the shared letter as
/// "m - Mistrust/Myopic", and that line is the likely origin of the error:
/// <see cref="Migration_CreateDatabase"/> wrote MISTRUST twice — once plain, once carrying
/// <c>Aliases = ["MYOPIC"]</c> — and gave both <c>typesContent</c> (PLAYER, EXIT, THING), which
/// wrongly admits players and wrongly omits rooms.</para>
///
/// <para>Two flags may share a letter here: <c>SharpObjectFlag.Symbol</c> documents itself as not
/// unique, and the seed already ships ABODE/ANSI on 'A', CHOWN_OK/COLOR on 'C' and NO_LEAVE/NO_TEL
/// on 'N', each pair separated by its type restrictions. Nothing needed changing to allow it.</para>
///
/// <para>The duplicate row is not simply deleted. Either document could be the one an object's
/// <c>edge_has_flags</c> edge points at — which of the two a lookup found was down to iteration
/// order — so the edges move to the surviving document first. Nothing loses a flag it had.</para>
///
/// <para>Idempotent and safe on a fresh database: the corrected seed writes one MISTRUST with the
/// right types and a separate MYOPIC, so every statement below either matches nothing or writes what
/// it already finds. This does <em>not</em> implement MYOPIC's display behaviour — that is a change
/// to the look/examine rendering path, deliberately left out.</para>
/// </summary>
public class Migration_RepairMistrustMyopic : IArangoMigration
{
	public long Id => 20260809_002;

	public string Name => "repair_mistrust_myopic";

	/// <summary>The MISTRUST document every other one collapses into: lowest <c>_key</c>, so the choice is stable.</summary>
	private const string Survivor =
		"""
		LET survivor = FIRST(
			FOR flag IN @@flags
				FILTER flag.Name == "MISTRUST"
				SORT flag._key
				RETURN flag)
		""";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			$$"""
			{{Survivor}}
			FOR flag IN @@flags
				FILTER flag.Name == "MISTRUST" AND flag._id != survivor._id
					FOR edge IN @@edges
						FILTER edge._to == flag._id
						UPDATE edge WITH { _to: survivor._id } IN @@edges
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@flags", DatabaseConstants.ObjectFlags },
				{ "@edges", DatabaseConstants.HasFlags }
			});

		// ArangoDB rejects a bind parameter the query does not mention, so every statement declares only
		// the collections it names.
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			$$"""
			{{Survivor}}
			FOR flag IN @@flags
				FILTER flag.Name == "MISTRUST" AND flag._id != survivor._id
				REMOVE flag IN @@flags
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@flags", DatabaseConstants.ObjectFlags }
			});

		// The survivor keeps its permissions and gains PennMUSH's type list; a stale MYOPIC alias goes.
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"""
			FOR flag IN @@flags
				FILTER flag.Name == "MISTRUST"
				UPDATE flag WITH {
					Symbol: "m",
					TypeRestrictions: @types,
					Aliases: REMOVE_VALUE(flag.Aliases == null ? [] : flag.Aliases, "MYOPIC")
				} IN @@flags
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@flags", DatabaseConstants.ObjectFlags },
				{ "types", DatabaseConstants.typesNonPlayer }
			});

		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"""
			UPSERT { Name: "MYOPIC" }
			INSERT { Name: "MYOPIC", Symbol: "m", System: true, TypeRestrictions: @types }
			UPDATE { Symbol: "m", System: true, TypeRestrictions: @types } IN @@flags
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@flags", DatabaseConstants.ObjectFlags },
				{ "types", DatabaseConstants.typesPlayer }
			});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
