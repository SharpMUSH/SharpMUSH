namespace SharpMUSH.Library.Models.Packages;

/// <summary>Where a plugin came from.</summary>
public enum PluginOrigin
{
	/// <summary>Shipped with the server. It can be turned off, not removed.</summary>
	BuiltIn,

	/// <summary>Deposited by a plugin package, or dropped into the installed plugins' directory by hand.</summary>
	Installed
}

/// <summary>What the last start did with a plugin it found.</summary>
public enum PluginBootStatus
{
	/// <summary>Loaded and running.</summary>
	Loaded,

	/// <summary>Turned off by an administrator.</summary>
	Disabled,

	/// <summary>Could not be loaded; the reason says why.</summary>
	Failed,

	/// <summary>Built for a plugin contract this server does not provide.</summary>
	Incompatible,

	/// <summary>Another plugin, found first, already uses its id.</summary>
	Duplicate,

	/// <summary>Installed since the last start; it has not run yet.</summary>
	NotStarted
}

/// <summary>What happens to a plugin at the next restart, compared with now.</summary>
public enum PluginPendingChange
{
	/// <summary>Nothing changes.</summary>
	None,

	/// <summary>Not running now; starts at the next restart.</summary>
	Starts,

	/// <summary>Running now; stops at the next restart.</summary>
	Stops
}

/// <summary>One plugin as the Packages page and <c>@plugin</c> show it.</summary>
/// <param name="Id">The plugin's id.</param>
/// <param name="Name">The plugin's display name (its id when it names none).</param>
/// <param name="Version">Its version, when known.</param>
/// <param name="Description">One line on what it adds, or null.</param>
/// <param name="Origin">Shipped with the server, or installed.</param>
/// <param name="Status">What the last start did with it.</param>
/// <param name="Reason">Why it is not running, for a failed, incompatible or duplicate plugin.</param>
/// <param name="Enabled">Whether it is meant to run: false once an administrator turns it off.</param>
/// <param name="Pending">What the next restart changes.</param>
/// <param name="Unloadable">Whether turning it off takes effect at once rather than at the next restart.</param>
/// <param name="Package">The installed package that deposited it, or null.</param>
/// <param name="Commands">How many commands it adds while running.</param>
/// <param name="Functions">How many functions it adds while running.</param>
/// <param name="Dependents">Installed packages that need it running.</param>
public sealed record PluginStatusDto(
	string Id,
	string Name,
	string? Version,
	string? Description,
	PluginOrigin Origin,
	PluginBootStatus Status,
	string? Reason,
	bool Enabled,
	PluginPendingChange Pending,
	bool Unloadable,
	string? Package,
	int Commands,
	int Functions,
	IReadOnlyList<string> Dependents);

/// <summary>The plugins and what the portal may do with them.</summary>
/// <param name="Plugins">Every plugin found, running or not.</param>
/// <param name="InstallAllowed">Whether this server lets the portal install plugin packages (remote or upload).</param>
/// <param name="RestartPending">Whether any plugin changes at the next restart.</param>
public sealed record PluginsResponse(IReadOnlyList<PluginStatusDto> Plugins, bool InstallAllowed, bool RestartPending);

/// <summary>What turning a plugin on or off did.</summary>
/// <param name="Plugin">The plugin afterwards.</param>
/// <param name="RemovedPackages">Packages that needed the plugin and were uninstalled with it.</param>
/// <param name="Notes">What the administrator should know, such as that a restart is needed.</param>
public sealed record PluginChangeResponse(PluginStatusDto Plugin, IReadOnlyList<string> RemovedPackages, IReadOnlyList<string> Notes);

/// <summary>An uploaded plugin package, ready for review.</summary>
/// <param name="Remote">The remote name to plan and apply it from.</param>
/// <param name="Path">The path to plan and apply it from.</param>
/// <param name="PackageId">The package's id.</param>
/// <param name="Version">The package's version.</param>
public sealed record PluginUploadResponse(string Remote, string Path, string PackageId, string Version);
