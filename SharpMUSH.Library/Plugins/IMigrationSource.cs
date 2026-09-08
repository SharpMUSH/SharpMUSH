using SharpMUSH.Library.Plugins.Storage;

namespace SharpMUSH.Library.Plugins;

/// <summary>One Lightning migration step: applied once, tracked in <c>meta</c> under <c>"mig:" + Id</c>.</summary>
/// <param name="Id">Stable identifier; changing it re-runs the step as if it had never applied.</param>
/// <param name="Apply">The step's body, given the Lightning accessor to read and write through.</param>
public sealed record LightningMigrationStep(string Id, Func<ILightningStorageAccessor, ValueTask> Apply);

/// <summary>
/// Phase 2a contribution surface for database migrations. A plugin entry type may implement this to
/// extend the schema or seed of whichever database backend is active. The pre-build
/// <c>PluginCatalog</c> collects every source; each provider reads only the contribution relevant to it
/// inside <c>Migrate()</c>, after its own built-in migration batch:
/// SurrealDB executes <see cref="SurrealStatements"/> and Lightning applies
/// <see cref="LightningSteps"/>.
/// Every member has an empty/no-op default so a plugin only implements the providers it supports.
/// </summary>
public interface IMigrationSource
{
	/// <summary>SurrealQL statements to run against SurrealDB after the built-in migration batch.</summary>
	IEnumerable<string> SurrealStatements => [];

	/// <summary>Lightning migration steps to run after the built-in migration batch, each applied once and tracked by <see cref="LightningMigrationStep.Id"/>.</summary>
	IEnumerable<LightningMigrationStep> LightningSteps => [];
}
