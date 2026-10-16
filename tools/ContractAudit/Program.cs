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
var source = new[] { "SharpMUSH.Implementation", "SharpMUSH.Library", "SharpMUSH.Server" }
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

var callbacks = source.Where(line => Regex.IsMatch(line.Text,
	@"\b(EvaluateAttribute\w*|CallAttribute\w*|FetchAttributeFunctionAsync|Queue\w*|Schedule\w*|RegisterRoute\w*)\s*\("))
	.Select(line => new { source = $"{line.File}:{line.Line}", invocation = line.Text.Trim(), status = "pending-contract-review" });
var output = new
{
	schema = 1,
	baseRevision = "38bab40c33ce362fe147b9b1d209367a02193b55",
	scope = "Implementation assembly + shipped default aliases. Re-run at the revision being reviewed.",
	entries = rows,
	callbackCandidates = callbacks,
	excluded = new[] { "runtime configured aliases", "game-defined functions/commands", "loaded/shipped plugin module registrations",
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

string[] Evidence(string kind, string name) => name is "json_map" or "json_mod"
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
