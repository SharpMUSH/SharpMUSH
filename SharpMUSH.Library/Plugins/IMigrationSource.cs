using System.Reflection;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2a contribution surface for database migrations, tagged per provider. A plugin entry type may
/// implement this to extend the schema/seed of whichever database backend is active. The pre-build
/// <c>PluginCatalog</c> collects every source; each provider reads only the contribution relevant to it
/// inside <c>Migrate()</c>, after its own built-in migration batch:
/// <list type="bullet">
///   <item>ArangoDB scans <see cref="ArangoMigrationAssembly"/> alongside its own and applies every
///   migration Id the database has not recorded yet, so a plugin installed into an already-migrated world
///   still gets its schema.</item>
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
}
