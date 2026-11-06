extern alias ScenePlugin;
extern alias DiscordPlugin;
extern alias LspPlugin;
extern alias McpPlugin;
extern alias WorldPlugin;

using SharpMUSH.Documentation;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Each shipped plugin's functions and commands against the help a game serves while the plugin is loaded: the engine's
/// helpfiles and the plugin's own <c>help</c> folder, the two places <c>TextFileService</c> indexes.
/// </summary>
public class PluginHelpParityTests
{
	private static readonly PluginBase[] Plugins =
	[
		new ScenePlugin::SharpMUSH.Plugins.Scene.ScenePlugin(),
		new DiscordPlugin::SharpMUSH.Plugins.Discord.DiscordPlugin(),
		new LspPlugin::SharpMUSH.Plugins.Lsp.LspPlugin(),
		new McpPlugin::SharpMUSH.Plugins.Mcp.McpPlugin(),
		new WorldPlugin::SharpMUSH.Plugins.World.WorldPlugin()
	];

	private static Helpfiles Engine()
	{
		var helpfiles = new Helpfiles(TestPaths.Helpfiles);
		helpfiles.Index();
		return helpfiles;
	}

	private static Helpfiles? Own(PluginBase plugin)
	{
		var folder = new DirectoryInfo(Path.Join(TestPaths.RepositoryRoot, plugin.GetType().Assembly.GetName().Name!, "help"));
		if (!folder.Exists)
		{
			return null;
		}

		var helpfiles = new Helpfiles(folder);
		helpfiles.Index();
		return helpfiles;
	}

	[Test]
	public async Task EveryShippedPluginFunctionAndCommandHasAHelpTopic()
	{
		var engine = Engine();

		var undocumented = Plugins.SelectMany(plugin =>
			{
				var own = Own(plugin);
				bool Found(string topic) => engine.FindEntry(topic) is not null || own?.FindEntry(topic) is not null;

				return plugin.GetFunctions().Select(definition => definition.Attribute.Name)
					.Where(name => !Found($"{name}()") && !Found(name))
					.Select(name => $"{plugin.Id}: {name}()")
					.Concat(plugin.GetCommands().Select(definition => definition.Attribute.Name)
						.Where(name => !Found(name))
						.Select(name => $"{plugin.Id}: {name}"));
			})
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(undocumented).IsEmpty();
	}
}
