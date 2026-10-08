using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The plugins as an administrator manages them, from the Packages page or <c>@plugin</c>: every plugin found,
/// whether it runs now and at the next restart, and turning one on or off.
/// </summary>
public interface IPluginAdministration
{
	/// <summary>Every plugin found at the last start or on disk now, and what the portal may do with them.</summary>
	Task<PluginsResponse> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Turns <paramref name="pluginId"/> on or off. Off is recorded for the next start, and takes effect at once
	/// for a plugin that only adds commands and functions. The installed packages that need the plugin are
	/// uninstalled first (a shipped one is recorded as declined, so the next start does not put it back); turning
	/// the plugin on again lets the next start reinstall a shipped one.
	/// </summary>
	Task<Result<PluginChangeResponse>> SetEnabledAsync(string pluginId, bool enabled,
		CancellationToken cancellationToken = default);
}
