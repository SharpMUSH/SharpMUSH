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

	/// <summary>The discovered dependency graph and the identity of the files and metadata it was read from.</summary>
	Task<PluginDependencySnapshot> ReadDependenciesAsync(CancellationToken cancellationToken = default);

	/// <summary>Packages and plugins that prevent the requested state change, without changing anything.</summary>
	Task<PluginStateBlockers> GetBlockersAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default);

	/// <summary>
	/// Turns a plugin on or off after checking dependencies inside the package operation gate. Dependents must be
	/// removed or turned off explicitly first. Enabling does not change any bundled package's declined choice.
	/// </summary>
	Task<Result<PluginChangeResponse>> SetEnabledAsync(string pluginId, bool enabled,
		CancellationToken cancellationToken = default);
}
