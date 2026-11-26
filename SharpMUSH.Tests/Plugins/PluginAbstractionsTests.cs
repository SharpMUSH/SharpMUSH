extern alias ScenePlugin;
extern alias DiscordPlugin;
extern alias WorldPlugin;

using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// The shipped plugins that others build on keep the types a dependent uses in an interface assembly of their own,
/// <c>&lt;plugin&gt;.Abstractions</c>, and register each of those services, so a dependent references the interface
/// and never the implementation.
/// </summary>
public class PluginAbstractionsTests
{
	public static IEnumerable<Func<(IServiceRegistrar Plugin, string[] Services)>> Plugins()
	{
		yield return () => (new ScenePlugin::SharpMUSH.Plugins.Scene.ScenePlugin(), ["ISceneService", "ISceneSystemBuilder"]);
		yield return () => (new WorldPlugin::SharpMUSH.Plugins.World.WorldPlugin(), ["IWorldMaps"]);
		yield return () => (new DiscordPlugin::SharpMUSH.Plugins.Discord.DiscordPlugin(),
			["IDiscordClientHandler", "IDiscordMessages", "IDiscordLinkedAccounts"]);
	}

	[Test]
	[MethodDataSource(nameof(Plugins))]
	public async Task EachServiceADependentUsesComesFromTheInterfaceAssembly(IServiceRegistrar plugin, string[] services)
	{
		var implementation = plugin.GetType().Assembly.GetName().Name!;
		var registered = new ServiceCollection();
		plugin.RegisterServices(registered);

		foreach (var name in services)
		{
			var service = registered.Select(descriptor => descriptor.ServiceType).FirstOrDefault(type => type.Name == name);
			await Assert.That(service?.Assembly.GetName().Name).IsEqualTo(implementation + ".Abstractions")
				.Because($"{implementation} registers {name}, which a plugin building on it compiles against without the implementation");
		}
	}
}
