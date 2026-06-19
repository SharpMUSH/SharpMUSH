using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2a contribution surface for dependency-injection registration. A plugin entry type may
/// implement this to add its own services into the host container.
///
/// Because DI registration must complete <b>during container construction</b> (before any post-build
/// hosted service runs), this seam is applied from the pre-build <c>PluginCatalog</c> inside
/// <c>Startup.ConfigureServices</c>, not from the post-build <c>PluginManager</c>. The catalog loads
/// every plugin once, then calls <see cref="RegisterServices"/> directly into the host
/// <see cref="IServiceCollection"/> before the rest of the engine's services are added.
/// </summary>
public interface IServiceRegistrar
{
	/// <summary>
	/// Add this plugin's services to the host container. Called once, pre-build, with the same
	/// <see cref="IServiceCollection"/> the engine itself registers into.
	/// </summary>
	void RegisterServices(IServiceCollection services);
}
