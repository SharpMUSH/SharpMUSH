using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Plugins;

/// <summary>A plugin the last start found, and what it did with it.</summary>
/// <param name="Id">The plugin's id (its <c>plugin.json</c> id, or its folder or file name).</param>
/// <param name="Name">Its display name from <c>plugin.json</c>, or null.</param>
/// <param name="Version">Its version, when known.</param>
/// <param name="Description">Its description from <c>plugin.json</c>, or null.</param>
/// <param name="Origin">Shipped or installed.</param>
/// <param name="Directory">The folder it was found in.</param>
/// <param name="Status">Loaded, or why not.</param>
/// <param name="Reason">The reason it is not loaded, in a sentence, or null.</param>
public sealed record PluginBootEntry(
	string Id,
	string? Name,
	string? Version,
	string? Description,
	PluginOrigin Origin,
	string Directory,
	PluginBootStatus Status,
	string? Reason);
