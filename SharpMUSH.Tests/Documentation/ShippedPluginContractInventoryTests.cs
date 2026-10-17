extern alias ScenePlugin;
extern alias DiscordPlugin;
extern alias LspPlugin;
extern alias McpPlugin;

using System.Text.Json;
using System.Xml.Linq;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Tests.Documentation;

public class ShippedPluginContractInventoryTests
{
	[Test]
	public async Task EveryShippedModuleAndItsActualGeneratedContractsAppearInTheInventory()
	{
		PluginBase[] plugins =
		[
			new ScenePlugin::SharpMUSH.Plugins.Scene.ScenePlugin(),
			new DiscordPlugin::SharpMUSH.Plugins.Discord.DiscordPlugin(),
			new LspPlugin::SharpMUSH.Plugins.Lsp.LspPlugin(),
			new McpPlugin::SharpMUSH.Plugins.Mcp.McpPlugin()
		];
		var shipped = XDocument.Load(Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Server", "SharpMUSH.Server.csproj"))
			.Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include"))
			.OfType<string>().Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
			.Where(name => name.StartsWith("SharpMUSH.Plugins.", StringComparison.Ordinal));
		await Assert.That(plugins.Select(plugin => plugin.GetType().Assembly.GetName().Name!)).IsEquivalentTo(shipped);
		using var census = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(TestPaths.RepositoryRoot, "docs", "audits", "softcode-contracts.json")));
		var modules = census.RootElement.GetProperty("shippedPluginModules").EnumerateArray().ToArray();
		await Assert.That(modules.Select(module => module.GetProperty("module").GetString()!)).IsEquivalentTo(plugins.Select(plugin => plugin.Id));
		var entries = census.RootElement.GetProperty("shippedPluginEntries").EnumerateArray().ToArray();
		foreach (var plugin in plugins)
		{
			var functions = plugin.GetFunctions().ToArray();
			var commands = plugin.GetCommands().ToArray();
			var reflectedFunctions = RegistryInventory.FunctionsFor(plugin.GetType().Assembly);
			var reflectedCommands = RegistryInventory.CommandsFor(plugin.GetType().Assembly);
			await Assert.That(functions.Select(definition => definition.Attribute.Name)).IsEquivalentTo(reflectedFunctions.Select(entry => entry.Name));
			await Assert.That(commands.Select(definition => definition.Attribute.Name)).IsEquivalentTo(reflectedCommands.Select(entry => entry.Name));
			var module = modules.Single(module => module.GetProperty("module").GetString() == plugin.Id);
			await Assert.That(module.GetProperty("functions").GetInt32()).IsEqualTo(functions.Length);
			await Assert.That(module.GetProperty("commands").GetInt32()).IsEqualTo(commands.Length);
			var rows = entries.Where(entry => entry.GetProperty("module").GetString() == plugin.Id).ToArray();
			await Assert.That(rows.Where(entry => entry.GetProperty("kind").GetString() == "function").Select(entry => entry.GetProperty("name").GetString()!))
				.IsEquivalentTo(functions.Select(definition => definition.Attribute.Name));
			await Assert.That(rows.Where(entry => entry.GetProperty("kind").GetString() == "command").Select(entry => entry.GetProperty("name").GetString()!))
				.IsEquivalentTo(commands.Select(definition => definition.Attribute.Name));
			foreach (var definition in functions) await Contract("function", definition.Attribute.Name, definition.Attribute);
			foreach (var definition in commands) await Contract("command", definition.Attribute.Name, definition.Attribute);
			await Assert.That(rows.Where(entry => entry.GetProperty("kind").GetString() == "switch").Select(entry => entry.GetProperty("name").GetString()!))
				.IsEquivalentTo(commands.SelectMany(definition => (definition.Attribute.Switches ?? []).Select(spelling => definition.Attribute.Name + "/" + spelling)));

			async Task Contract(string kind, string name, Attribute attribute)
			{
				var entry = rows.Single(entry => entry.GetProperty("kind").GetString() == kind && entry.GetProperty("name").GetString() == name);
				await Assert.That(entry.GetProperty("metadata").GetRawText()).IsEqualTo(JsonSerializer.Serialize(RegistryInventory.Metadata(attribute)));
				await Assert.That(entry.GetProperty("source").GetArrayLength()).IsGreaterThan(0);
				await Assert.That(entry.GetProperty("review").GetProperty("complete").GetBoolean()).IsFalse();
				await Assert.That(entry.GetProperty("status").GetString()).IsEqualTo("pending-behavioral-review");
			}
		}
	}
}
