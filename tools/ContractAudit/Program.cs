extern alias ScenePlugin;
extern alias DiscordPlugin;
extern alias LspPlugin;
extern alias McpPlugin;

using System.Diagnostics;
using SharpMUSH.Tools.ContractAudit;
using System.Xml.Linq;
using SharpMUSH.Library.Plugins;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Generated;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Tests;

if (args.Length != 2)
{
	Console.Error.WriteLine("usage: ContractAudit <repository root> <output.json>");
	return 2;
}

var root = Path.GetFullPath(args[0]);
PluginBase[] shippedPlugins =
[
	new ScenePlugin::SharpMUSH.Plugins.Scene.ScenePlugin(),
	new DiscordPlugin::SharpMUSH.Plugins.Discord.DiscordPlugin(),
	new LspPlugin::SharpMUSH.Plugins.Lsp.LspPlugin(),
	new McpPlugin::SharpMUSH.Plugins.Mcp.McpPlugin()
];
var shippedProjects = XDocument.Load(Path.Join(root, "SharpMUSH.Server", "SharpMUSH.Server.csproj"))
	.Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include"))
	.OfType<string>().Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
	.Where(name => name.StartsWith("SharpMUSH.Plugins.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
if (!shippedProjects.SetEquals(shippedPlugins.Select(plugin => plugin.GetType().Assembly.GetName().Name!)))
	throw new InvalidOperationException("Shipped plugin references changed; update the audit's explicit module inventory.");
var sourceProjects = new[] { "SharpMUSH.Implementation", "SharpMUSH.Library", "SharpMUSH.Server" }.Concat(shippedProjects).Order(StringComparer.Ordinal).ToArray();
var source = sourceProjects
	.SelectMany(project => Directory.EnumerateFiles(Path.Join(root, project), "*.cs", SearchOption.AllDirectories))
	.Where(path => !path.Contains("/obj/") && !path.Contains("/bin/"))
	.Order(StringComparer.Ordinal)
	.SelectMany(path => File.ReadLines(path).Select((text, index) => (File: Path.GetRelativePath(root, path), Line: index + 1, Text: text)))
	.ToArray();
var rows = new List<object>();
var functions = FunctionLibrary.CreateMetadata();
var commands = CommandLibrary.CreateMetadata();
if (!functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RegistryInventory.Functions.Select(entry => entry.Name))
	|| !commands.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RegistryInventory.Commands.Select(entry => entry.Name)))
	throw new InvalidOperationException("Generated metadata and reflection disagree; run RegistryContractAuditTests before exporting.");

foreach (var entry in RegistryInventory.Functions.OrderBy(entry => entry.Name, StringComparer.Ordinal))
{
	var metadata = functions[entry.Name];
	RequireSame(metadata, entry.Attribute);
	rows.Add(new
	{
		kind = "function",
		name = entry.Name,
		origin = entry.Origin,
		metadata = RegistryInventory.Metadata(metadata),
		source = Locations(entry.Name, "Functions"),
		status = "pending-behavioral-review",
		reconciled = "reflection/generated-metadata",
		review = Review("function", entry.Name),
		evidence = Evidence("function", entry.Name)
	});
}
foreach (var entry in RegistryInventory.Commands.OrderBy(entry => entry.Name, StringComparer.Ordinal))
{
	var metadata = commands[entry.Name];
	RequireSame(metadata, entry.Attribute);
	rows.Add(new
	{
		kind = "command",
		name = entry.Name,
		origin = entry.Origin,
		metadata = RegistryInventory.Metadata(metadata),
		source = Locations(entry.Name, "Commands"),
		status = "pending-behavioral-review",
		reconciled = "reflection/generated-metadata",
		review = Review("command", entry.Name),
		evidence = Evidence("command", entry.Name)
	});
	foreach (var spelling in metadata.Switches ?? [])
		rows.Add(new
		{
			kind = "switch",
			name = entry.Name + "/" + spelling,
			target = entry.Name,
			status = "pending-behavioral-review",
			evidence = Evidence("command", entry.Name)
		});
}
AddAliases("function-alias", AliasOptions.Default.FunctionAliases, functions.Keys);
AddAliases("command-alias", AliasOptions.Default.CommandAliases, commands.Keys);

foreach (var definition in EventCatalogue.All.OrderBy(definition => definition.Name, StringComparer.Ordinal))
	rows.Add(new
	{
		kind = "event-callback",
		name = definition.Name,
		metadata = definition,
		status = "pending-behavioral-review",
		evidence = new[]
		{
			"SharpMUSH.Library/Definitions/EventCatalogue.cs", "SharpMUSH.Library/Definitions/EventArguments.cs",
			"docs/audits/softcode-contracts.md#callback-entry-points-and-remaining-review"
		}
	});

// These are the plugin's actual generated runtime registrations, without initializing services or evaluating delegates.
var pluginRows = new List<object>();
var modules = new List<object>();
foreach (var plugin in shippedPlugins.OrderBy(plugin => plugin.Id, StringComparer.Ordinal))
{
	var assembly = plugin.GetType().Assembly;
	var reflectedFunctions = RegistryInventory.FunctionsFor(assembly);
	var reflectedCommands = RegistryInventory.CommandsFor(assembly);
	var registeredFunctions = plugin.GetFunctions().ToDictionary(definition => definition.Attribute.Name, StringComparer.Ordinal);
	var registeredCommands = plugin.GetCommands().ToDictionary(definition => definition.Attribute.Name, StringComparer.Ordinal);
	if (!registeredFunctions.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(reflectedFunctions.Select(entry => entry.Name))
		|| !registeredCommands.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(reflectedCommands.Select(entry => entry.Name)))
		throw new InvalidOperationException($"Plugin {plugin.Id} generated registrations and reflection disagree.");
	foreach (var entry in reflectedFunctions.OrderBy(entry => entry.Name, StringComparer.Ordinal))
	{
		var attribute = registeredFunctions[entry.Name].Attribute;
		RequireSame(attribute, entry.Attribute);
		pluginRows.Add(new
		{
			module = plugin.Id,
			kind = "function",
			name = entry.Name,
			origin = entry.Origin,
			metadata = RegistryInventory.Metadata(attribute),
			source = ModuleLocations(assembly.GetName().Name!, entry.Name),
			status = "pending-behavioral-review",
			reconciled = "reflection/generated-runtime-registration",
			review = Review("function", entry.Name)
		});
	}
	foreach (var entry in reflectedCommands.OrderBy(entry => entry.Name, StringComparer.Ordinal))
	{
		var attribute = registeredCommands[entry.Name].Attribute;
		RequireSame(attribute, entry.Attribute);
		pluginRows.Add(new
		{
			module = plugin.Id,
			kind = "command",
			name = entry.Name,
			origin = entry.Origin,
			metadata = RegistryInventory.Metadata(attribute),
			source = ModuleLocations(assembly.GetName().Name!, entry.Name),
			status = "pending-behavioral-review",
			reconciled = "reflection/generated-runtime-registration",
			review = Review("command", entry.Name)
		});
		foreach (var spelling in attribute.Switches ?? [])
			pluginRows.Add(new
			{
				module = plugin.Id,
				kind = "switch",
				name = entry.Name + "/" + spelling,
				target = entry.Name,
				status = "pending-behavioral-review"
			});
	}
	modules.Add(new
	{
		module = plugin.Id,
		assembly = assembly.GetName().Name,
		functions = registeredFunctions.Count,
		commands = registeredCommands.Count,
		switches = reflectedCommands.Sum(entry => entry.Attribute.Switches?.Length ?? 0),
		status = "registration-reconciled-behavior-pending"
	});
}

var callbacks = source.Where(line => Regex.IsMatch(line.Text,
	@"\b(EvaluateAttribute\w*|CallAttribute\w*|FetchAttributeFunctionAsync|Queue\w*|Schedule\w*|RegisterRoute\w*)\s*\("))
	.Select(line => new { source = $"{line.File}:{line.Line}", invocation = line.Text.Trim(), status = "pending-contract-review" });
var output = new
{
	schema = 2,
	provenance = SourceProvenance.Capture(root, Git("ls-files", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries)),
	scope = "Implementation assembly, shipped default aliases and every plugin module referenced by the Server project. Registration reconciliation is not behavioral completion.",
	entries = rows,
	shippedPluginModules = modules,
	shippedPluginEntries = pluginRows,
	callbackCandidates = callbacks,
	excluded = new[] { "runtime configured aliases", "game-defined functions/commands", "third-party/loaded plugin registrations beyond shipped modules",
		"dynamic HTTP/package callbacks", "live analysis identity and visibility" }
		.Select(surface => new { surface, total = (int?)null, status = "unknown-pending-inventory" })
};
// One record per line keeps the complete machine-readable census reviewable in Git.
var document = JsonSerializer.SerializeToElement(output);
var properties = document.EnumerateObject().Select(property =>
	property.Value.ValueKind == JsonValueKind.Array
		? $"  \"{property.Name}\": [\n" + string.Join(",\n", property.Value.EnumerateArray().Select(value => "    " + value.GetRawText())) + "\n  ]"
		: $"  \"{property.Name}\": {property.Value.GetRawText()}");
await File.WriteAllTextAsync(args[1], "{\n" + string.Join(",\n", properties) + "\n}\n");
Console.WriteLine($"Wrote {functions.Count} functions, {commands.Count} commands and their shipped aliases/switches to {args[1]}.");
return 0;

void RequireSame<T>(T generated, T reflected) where T : Attribute
{
	if (JsonSerializer.Serialize(RegistryInventory.Metadata(generated)) != JsonSerializer.Serialize(RegistryInventory.Metadata(reflected)))
		throw new InvalidOperationException("Generated metadata differs from reflected declaration.");
}

string[] Locations(string name, string directory) => source.Where(line => line.File.StartsWith($"SharpMUSH.Implementation/{directory}/", StringComparison.Ordinal) && Regex.IsMatch(line.Text,
	@"\bName\s*=\s*""" + Regex.Escape(name) + @"""", RegexOptions.IgnoreCase))
	.Select(line => $"{line.File}:{line.Line}").ToArray();

string[] Evidence(string kind, string name) => name == "unique"
	? ["SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs", "SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs",
		"tools/client-tests/monaco-signatures.test.mjs", "SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpfunc.md",
		"docs/audits/softcode-contracts.md#unique-comparison-labels"]
	: name is "setunion" or "setdiff" or "setinter" or "setsymdiff"
	? ["SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs", "SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs",
		"tools/client-tests/monaco-signatures.test.mjs", "SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpfunc.md",
		"docs/audits/softcode-contracts.md#set-operation-overloads"]
	: name is "sortkey" or "index" or "munge"
	? ["SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs", "SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs",
		"tools/client-tests/monaco-signatures.test.mjs", "docs/audits/softcode-contracts.md#list-transformation-signatures"]
	: name == "mix"
	? ["SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs", "SharpMUSH.Tests/Functions/MixFunctionCallShapeTests.cs",
		"tools/client-tests/monaco-signatures.test.mjs", "SharpMUSH.Documentation/Helpfiles/SharpMUSH/mix-function.md",
		"docs/audits/softcode-contracts.md#mix-optional-list-and-delimiter-labels"]
	: name is "json_query" or "json_fill"
	? ["SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs", "SharpMUSH.Tests/Functions/JsonFunctionUnitTests.cs",
		$"SharpMUSH.Documentation/Helpfiles/SharpMUSH/{name.Replace('_', '-')}-function.md",
		"tools/client-tests/monaco-signatures.test.mjs", "docs/audits/softcode-contracts.md#json-query-actions-and-fill-pairs"]
	: name is "json_map" or "json_mod"
	? ["SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs", "SharpMUSH.Tests/Functions/JsonFunctionUnitTests.cs",
		$"SharpMUSH.Documentation/Helpfiles/SharpMUSH/{name.Replace('_', '-')}-function.md", "docs/audits/softcode-contracts.md#json-contracts"]
	: ["SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs", $"docs/audits/softcode-contracts.md#{kind}-consumers"];

void AddAliases(string kind, Dictionary<string, string[]> aliases, IEnumerable<string> registered)
{
	var names = registered.ToHashSet(StringComparer.OrdinalIgnoreCase);
	foreach (var (target, spellings) in aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal))
	{
		if (!names.Contains(target)) throw new InvalidOperationException($"Dangling {kind} target: {target}");
		foreach (var spelling in spellings.Order(StringComparer.Ordinal))
			rows.Add(new
			{
				kind,
				name = spelling,
				target,
				status = "pending-behavioral-review",
				evidence = new[] { "SharpMUSH.Configuration/Options/AliasOptions.cs", "SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs" }
			});
	}
}

string[] ModuleLocations(string project, string name) => source.Where(line => line.File.StartsWith(project + "/", StringComparison.Ordinal)
	&& Regex.IsMatch(line.Text, @"\bName\s*=\s*""" + Regex.Escape(name) + @"""", RegexOptions.IgnoreCase))
	.Select(line => $"{line.File}:{line.Line}").ToArray();

object Review(string kind, string name) => new
{
	complete = false,
	reviewedFacets = kind == "function" && name is "json_map" or "json_mod" or "json_query" or "json_fill" or "sortkey" or "index" or "munge" or "setunion" or "setdiff" or "setinter" or "setsymdiff" or "unique" or "mix"
		? new[] { "registration-metadata", "parameter-labels", "focused-parser-call-shapes", "analysis-portal-monaco-signatures" }
		: new[] { "registration-metadata" },
	pendingFacets = new[] { "full-argument-semantics", "evaluation-and-laziness", "authority-and-permissions", "errors-and-boundaries",
		"resource-limits", "compatibility", "live-consumer-identity" }
};

string Git(params string[] arguments)
{
	var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
	foreach (var argument in arguments) start.ArgumentList.Add(argument);
	using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not read source revision.");
	var output = process.StandardOutput.ReadToEnd();
	var error = process.StandardError.ReadToEnd();
	process.WaitForExit();
	if (process.ExitCode != 0) throw new InvalidOperationException($"Could not read source revision: {error}");
	return output.Trim();
}
