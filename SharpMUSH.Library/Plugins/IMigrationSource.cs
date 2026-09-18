using SharpMUSH.Library.Plugins.Storage;

namespace SharpMUSH.Library.Plugins;

/// <summary>One Lightning migration step: applied once, tracked in <c>meta</c> under <c>"mig:" + Id</c>.</summary>
/// <param name="Id">Stable identifier; changing it re-runs the step as if it had never applied.</param>
/// <param name="Apply">The step's body, given the Lightning accessor to read and write through.</param>
public sealed record LightningMigrationStep(string Id, Func<ILightningStorageAccessor, ValueTask> Apply);

/// <summary>
/// Phase 2a contribution surface for database migrations. A plugin entry type may implement this to
/// extend the schema or seed of the world. The pre-build <c>PluginCatalog</c> collects every source, and
/// the Lightning provider applies each source's <see cref="LightningSteps"/> inside <c>Migrate()</c>,
/// after its own built-in migration batch.
/// </summary>
public interface IMigrationSource
{
	/// <summary>Lightning migration steps to run after the built-in migration batch, each applied once and tracked by <see cref="LightningMigrationStep.Id"/>.</summary>
	IEnumerable<LightningMigrationStep> LightningSteps => [];
}
