using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Services;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// The Scene plugin's assembly as one host loaded it. Every host loads the plugin into a collectible context of its
/// own, and a test run starts several, so the first such assembly in the app domain may be another host's, whose types
/// this host's services do not know.
/// </summary>
internal static class ScenePluginAssembly
{
	public static Assembly Of(IServiceProvider services) =>
		services.GetRequiredService<PluginCatalog>().Plugins
			.Select(plugin => plugin.GetType().Assembly)
			.First(assembly => assembly.GetName().Name == "SharpMUSH.Plugins.Scene");
}
