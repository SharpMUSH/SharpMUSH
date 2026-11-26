using System.Reflection;
using System.Runtime.Loader;
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

	/// <summary>
	/// The Scene System's interface assembly (<c>ISceneService</c> and the models), as the same host's Scene plugin
	/// loaded it: from <c>plugins/scene/</c>, into that plugin's own context.
	/// </summary>
	public static Assembly AbstractionsOf(IServiceProvider services) =>
		AssemblyLoadContext.GetLoadContext(Of(services))!.Assemblies
			.First(assembly => assembly.GetName().Name == "SharpMUSH.Plugins.Scene.Abstractions");
}
