namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Metadata loaded from a <c>plugin.json</c> file placed next to a plugin DLL. Drives discovery,
/// load order, and compatibility checks. When absent, the loader falls back to the
/// <see cref="IPlugin"/> instance's own properties.
/// </summary>
/// <param name="Id">Stable machine identity (must match the <see cref="IPlugin.Id"/>).</param>
/// <param name="Version">Semantic version string.</param>
/// <param name="Dependencies">Ids of plugins this one must load after.</param>
/// <param name="Priority">Tie-break ordering; lower loads first.</param>
/// <param name="MinServerVersion">Minimum SharpMUSH server version required, or null for any.</param>
/// <param name="SharedAssemblies">
/// Simple assembly NAMES (no extension) the host must share into this plugin's collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> rather than letting the plugin reload its own
/// copy. Needed when the plugin calls a host service whose method signatures reference a third-party type
/// (e.g. a storage plugin that uses the providers' DB-client connections via a host-shared accessor): the
/// client type must unify across the boundary, which only happens if the host's copy is authoritative.
/// The host already has these loaded (it references the providers), so sharing by name works without the
/// plugin framework referencing the client packages. Null/absent means none.
/// </param>
public sealed record PluginManifest(
	string Id,
	string Version,
	IReadOnlyList<string> Dependencies,
	int Priority,
	string? MinServerVersion,
	IReadOnlyList<string>? SharedAssemblies = null);
