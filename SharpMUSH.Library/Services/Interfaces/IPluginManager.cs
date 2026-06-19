namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Discovers plugin DLLs under the <c>plugins/</c> directory, resolves load order, and registers
/// each plugin's command/function contributions into the live engine libraries.
/// </summary>
public interface IPluginManager
{
	/// <summary>
	/// Scan, order, load, and register every discoverable plugin. Each plugin is isolated: a single
	/// failing plugin is logged and skipped without aborting the rest of the load.
	/// </summary>
	Task LoadAllAsync(CancellationToken cancellationToken);
}
