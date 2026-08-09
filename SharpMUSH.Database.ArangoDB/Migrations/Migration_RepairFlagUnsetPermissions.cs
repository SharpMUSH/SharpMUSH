using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Repairs the thirteen object-flag seeds that <see cref="Migration_CreateDatabase"/> wrote with the
/// property spelled <c>UnSetPermissions</c> (capital S) instead of <c>UnsetPermissions</c>. The seed
/// used anonymous objects, the serializer preserves PascalCase verbatim, and
/// <c>SharpObjectFlagQueryResult</c> reads <c>UnsetPermissions</c> — so the two never met and those
/// flags read back with no unset restriction at all.
///
/// <para>That is not a cosmetic mismatch. <c>ManipulateSharpObjectService.SetOrUnsetFlag</c> treats an
/// absent or empty permission list as "unrestricted" (matching PennMUSH's <c>can_set_flag_generic()</c>,
/// where a flag with no F_* permission bits is settable by anyone who controls the object), so the
/// missing property let anyone who controls an object clear ROYALTY, SUSPECT, NO_LOG, GAGGED, MISTRUST
/// and PARANOID off it.
///
/// <para>The standing decision is that a pre-existing SharpMUSH database is dropped and re-migrated,
/// which would also fix this. This migration exists anyway because it is three lines of AQL and it
/// removes the need to trust that every operator did the drop — silently keeping an unenforced
/// ROYALTY unset permission is not a failure mode worth leaving to convention.</para>
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
