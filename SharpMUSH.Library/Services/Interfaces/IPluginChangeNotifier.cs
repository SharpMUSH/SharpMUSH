namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Generic signal that the set of loaded plugins changed at runtime (a plugin was unloaded or reloaded).
/// Implemented in the Server layer (it broadcasts a generic "plugins changed" message to connected portal
/// clients over SignalR), and consumed by the <c>IPluginManager</c> after a successful unload/reload.
///
/// <para>This interface is deliberately type-free: it carries no plugin-specific payload. The client reacts
/// to the signal by forcing a hard browser refresh, which fully tears down and rebuilds the WASM runtime —
/// the only way to reclaim a compiled component assembly that was loaded into the browser (Mono-WASM
/// <c>AssemblyLoadContext.Unload()</c> is a no-op). Loading a NEW plugin does not raise this; only unload
/// (and reload, which unloads first) does.</para>
/// </summary>
public interface IPluginChangeNotifier
{
	/// <summary>
	/// Broadcast that the loaded-plugin set changed. Best-effort: failures must not abort the unload that
	/// triggered them. No-op when there is no transport (e.g. unit tests with no notifier wired).
	/// </summary>
	Task NotifyPluginsChangedAsync();
}
