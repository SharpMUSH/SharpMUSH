using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Repairs the thirteen object-flag seeds that <see cref="Migration_CreateDatabase"/> wrote with the
/// property spelled <c>UnSetPermissions</c> (capital S) instead of <c>UnsetPermissions</c>. The seed
/// used anonymous objects, which the serializer writes verbatim, while
/// <c>SharpObjectFlagQueryResult</c> reads <c>UnsetPermissions</c>.
///
/// <para><b>Those flags were still enforced.</b> <c>Core.Arango</c>'s <c>ArangoJsonSerializer</c> is
/// constructed from <c>JsonSerializerDefaults.Web</c>, which sets <c>PropertyNameCaseInsensitive</c>,
/// so the misspelled key landed on the right member and nothing was ever settable that should not have
/// been. This migration removes a coincidence, not a permission bypass — say so plainly, because the
/// mismatch reads like a bypass and the next person to find it will assume it was one.</para>
///
/// <para>The coincidence is still worth removing. AQL attribute access is case-sensitive, so the first
/// query that filters or projects on this field drops the restriction on twelve flags with no error;
/// changing the serializer's naming policy would do the same; and a document carrying both spellings
/// resolves last-key-wins rather than to the correct one (reachable only if the <c>System</c> guard on
/// <c>UpdateObjectFlagAsync</c>'s merge UPDATE ever moves, since all thirteen are system flags).</para>
///
/// <para>The standing decision is that a pre-existing SharpMUSH database is dropped and re-migrated,
/// which would also fix this. This migration exists anyway because it is three lines of AQL, and
/// leaving correctness resting on a third-party serializer default is not worth the alternative of
/// trusting that every operator did the drop.</para>
///
/// <para>Idempotent and safe to run on a fresh database: the corrected seed already writes the right
/// property, so the UPDATE writes the same value it finds. <c>keepNull: false</c> is what removes the
/// stale misspelled property rather than setting it to null.</para>
///
/// <para>Arango only. Memgraph and SurrealDB seed their flags from positional named tuples
/// (<c>MemgraphDatabase.Migration.cs</c>, <c>SurrealDatabase.Migration.cs</c>) whose fields the compiler
/// checks, so the same class of typo cannot occur there — verified by reading both seeds, and by the
/// round-trip test running green on all three providers.</para>
/// </summary>
public class Migration_RepairFlagUnsetPermissions : IArangoMigration
{
	public long Id => 20260809_001;

	public string Name => "repair_flag_unset_permissions";

	/// <summary>
	/// The flags <see cref="Migration_CreateDatabase"/> misspelled, with the permission list each was
	/// meant to carry. Kept as data rather than re-deriving from the seed so the repair is auditable
	/// against the migration history.
	/// </summary>
	private static readonly (string Name, string[] UnsetPermissions)[] Repairs =
	[
		("NOSPOOF", DatabaseConstants.permissionsODark),
		("GAGGED", DatabaseConstants.permissionsWizard),
		("JURY_OK", DatabaseConstants.permissionsRoyalty),
		("MISTRUST", DatabaseConstants.permissionsTrusted),
		("ROYALTY", DatabaseConstants.permissionsTrusted),
		("SUSPECT", DatabaseConstants.permissionsWizard),
		("CHAN_USEFIRSTMATCH", DatabaseConstants.permissionsTrusted),
		("NO_LOG", DatabaseConstants.permissionsWizard),
		("PARANOID", DatabaseConstants.permissionsODark),
		("MONIKER", DatabaseConstants.permissionsRoyalty),
		("GOING", DatabaseConstants.permissionsWizard),
		("GOING_TWICE", DatabaseConstants.permissionsWizard)
	];

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle) =>
		await migrator.Context.Query.ExecuteAsync<object>(
			handle,
			"""
			FOR repair IN @repairs
				FOR flag IN @@c
					FILTER flag.Name == repair.name
					UPDATE flag WITH { UnsetPermissions: repair.perms, UnSetPermissions: null } IN @@c
						OPTIONS { keepNull: false }
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ObjectFlags },
				{
					"repairs",
					Repairs.Select(repair => new { name = repair.Name, perms = repair.UnsetPermissions }).ToArray()
				}
			});

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
