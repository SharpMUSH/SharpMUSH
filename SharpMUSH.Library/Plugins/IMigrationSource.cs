using System.Reflection;
using SharpMUSH.Library.Plugins.Storage;

namespace SharpMUSH.Library.Plugins;

/// <summary>One Lightning migration step: applied once, tracked in <c>meta</c> under <c>"mig:" + Id</c>.</summary>
/// <param name="Id">Stable identifier; changing it re-runs the step as if it had never applied.</param>
/// <param name="Apply">The step's body, given the Lightning accessor to read and write through.</param>
public sealed record LightningMigrationStep(string Id, Func<ILightningStorageAccessor, ValueTask> Apply);

/// <summary>
/// Phase 2a contribution surface for database migrations, tagged per provider. A plugin entry type may
/// implement this to extend the schema/seed of whichever database backend is active. The pre-build
/// <c>PluginCatalog</c> collects every source; each provider reads only the contribution relevant to it
/// inside <c>Migrate()</c>, after its own built-in migration batch:
/// <list type="bullet">
///   <item>ArangoDB treats <see cref="ArangoMigrationAssembly"/> as a migration stream of its own, tracked
///   separately from the engine's: it applies the Ids that assembly has not recorded yet, in Id order, and
///   nothing the engine has applied blocks them. So a plugin installed into an already-migrated world still
///   gets its schema even though its migrations predate the engine's newest.</item>
///   <item>Memgraph runs each statement in <see cref="CypherStatements"/>.</item>
///   <item>SurrealDB runs each statement in <see cref="SurrealStatements"/>.</item>
/// </list>
/// Every member has an empty/no-op default so a plugin only implements the providers it supports.
/// </summary>
public interface IMigrationSource
{
	/// <summary>
	/// The assembly containing the plugin's ArangoDB <c>IArangoMigration</c> types, or <c>null</c> when
	/// the plugin contributes no Arango migrations.
	/// </summary>
	Assembly? ArangoMigrationAssembly => null;

	/// <summary>Cypher statements to run against Memgraph after the built-in migration batch.</summary>
	IEnumerable<string> CypherStatements => [];

	/// <summary>SurrealQL statements to run against SurrealDB after the built-in migration batch.</summary>
	IEnumerable<string> SurrealStatements => [];

	/// <summary>Lightning migration steps to run after the built-in migration batch, each applied once and tracked by <see cref="LightningMigrationStep.Id"/>.</summary>
	IEnumerable<LightningMigrationStep> LightningSteps => [];
}
