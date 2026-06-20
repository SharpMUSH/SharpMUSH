using OneOf;
using OneOf.Types;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Discovers plugin DLLs under the <c>plugins/</c> directory, resolves load order, and registers
/// each plugin's command/function contributions into the live engine libraries. Also drives the
/// Phase 3 runtime unload/reload of <i>unloadable</i> plugins (those contributing only commands/functions
/// via a collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/>).
/// </summary>
public interface IPluginManager
{
	/// <summary>
	/// Scan, order, load, and register every discoverable plugin. Each plugin is isolated: a single
	/// failing plugin is logged and skipped without aborting the rest of the load.
	/// </summary>
	Task LoadAllAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Unload an <i>unloadable</i> plugin at runtime: remove the command/function entries it registered from
	/// the live libraries, then dispose its collectible plugin loader so its
	/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> can be reclaimed by the GC. Returns an
	/// <see cref="Error{T}"/> when the plugin is unknown or is load-once (it contributes DI/migration/flag/
	/// bridge state that the container, database, or bridge has captured and that a restart alone can clear).
	/// </summary>
	Task<OneOf<Success, Error<string>>> UnloadAsync(string pluginId);

	/// <summary>
	/// Reload an <i>unloadable</i> plugin at runtime: unload it (see <see cref="UnloadAsync"/>) then load its
	/// DLL afresh from disk and re-register its commands/functions. Returns an <see cref="Error{T}"/> with the
	/// same restraints as <see cref="UnloadAsync"/> (unknown id, or load-once plugin requiring a restart).
	/// </summary>
	Task<OneOf<Success, Error<string>>> ReloadAsync(string pluginId);
}
