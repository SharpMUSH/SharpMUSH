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
public sealed record PluginManifest(
	string Id,
	string Version,
	IReadOnlyList<string> Dependencies,
	int Priority,
	string? MinServerVersion);
