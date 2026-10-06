using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;

namespace SharpMUSH.Tools.ClientData;

/// <summary>
/// Builds the data files the portal's softcode editor and help drawer read from <c>data/</c>: <c>mush-defs.json</c> (signatures and help text) and the
/// flat name lists <c>mush-functions.json</c> and <c>mush-commands.json</c> it falls back to.
/// </summary>
/// <remarks>
/// <para>The names, arity, parameter names and switches come off the <c>[SharpFunction]</c> and
/// <c>[SharpCommand]</c> attributes; the help text comes from the shipped helpfiles. Neither copy is
/// edited by hand (#1248): while they were, a signature change left <c>lstats()</c>'s
/// <c>parameterNames</c> stale (#1241) and the flat list never learned about <c>u</c>. Nor are they
/// checked in: the portal's build writes them, so a help change never leaves a stale copy to merge.</para>
/// <para>Shipped aliases get an entry of their own carrying the target's signature, since the editor
/// looks a call up by the name typed.</para>
/// </remarks>
public static class ClientDataGenerator
{
	/// <summary>How many non-empty help lines the hover tooltip shows.</summary>
	private const int PreviewLines = 3;

	private static readonly JsonSerializerOptions Output = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>Every generated file, by file name, built from the helpfiles under <paramref name="helpfiles"/>.</summary>
	public static IReadOnlyDictionary<string, string> Files(DirectoryInfo helpfiles)
	{
		var help = new Helpfiles(helpfiles);
		help.Index();

		var functions = Registry.Functions
			.Select(f => (f.Name, Help: FunctionHelp(help, f.Name), Entry: new JsonObject
			{
				["maxArgs"] = f.MaxArgs,
				["minArgs"] = f.MinArgs,
				["parameterNames"] = Array(f.ParameterNames)
			}))
			.Concat(Aliases(AliasOptions.Default.FunctionAliases, Registry.Functions.Select(f => f.Name))
				.Select(alias => (Name: alias.Alias, Help: FunctionHelp(help, alias.Alias) ?? FunctionHelp(help, alias.Target),
					Entry: FunctionEntry(alias.Target))))
			.ToList();

		var commands = Registry.Commands
			.Select(c => (c.Name, Help: FullHelp(help, c.Name), Entry: CommandEntry(c)))
			.Concat(Aliases(AliasOptions.Default.CommandAliases, Registry.Commands.Select(c => c.Name))
				.Select(alias => (Name: alias.Alias, Help: FullHelp(help, alias.Alias) ?? FullHelp(help, alias.Target),
					Entry: CommandEntry(Registry.Commands.First(c => c.Name.Equals(alias.Target, StringComparison.OrdinalIgnoreCase))))))
			.ToList();

		var definitions = new JsonObject
		{
			["commands"] = Section(commands),
			["functions"] = Section(functions)
		};

		return new Dictionary<string, string>
		{
			["mush-defs.json"] = Serialize(definitions),
			["mush-functions.json"] = Serialize(Array(Names(functions.Select(f => f.Name.ToLowerInvariant())))),
			["mush-commands.json"] = Serialize(Array(Names(commands.Select(c => c.Name.ToUpperInvariant()))))
		};
	}

	private static JsonObject FunctionEntry(string name)
	{
		var attribute = Registry.Functions.First(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		return new JsonObject
		{
			["maxArgs"] = attribute.MaxArgs,
			["minArgs"] = attribute.MinArgs,
			["parameterNames"] = Array(attribute.ParameterNames)
		};
	}

	private static JsonObject CommandEntry(SharpMUSH.Library.Attributes.SharpCommandAttribute attribute) => new()
	{
		["maxArgs"] = attribute.MaxArgs,
		["minArgs"] = attribute.MinArgs,
		["parameterNames"] = Array(attribute.ParameterNames),
		["switches"] = Array(attribute.Switches ?? [])
	};

	private static string? FunctionHelp(Helpfiles help, string name)
		=> FullHelp(help, $"{name}()") ?? FullHelp(help, name);

	private static string? FullHelp(Helpfiles help, string topic)
	{
		var entry = help.FindHelpEntry(topic);
		return entry?.Article?.Markdown ?? entry?.Markdown;
	}

	/// <summary>The drawer supplies its own title, so remove the structural article H1.</summary>
	private static string WithoutHeader(string text)
	{
		var heading = HelpArticleParser.Headings(text).FirstOrDefault(heading => heading.Level == 1);
		return heading?.Span.Start == 0 ? text[(heading.Span.End + 1)..] : text;
	}

	/// <summary>The shipped aliases whose target is registered and which are not registered names themselves.</summary>
	private static IEnumerable<(string Alias, string Target)> Aliases(Dictionary<string, string[]> aliases, IEnumerable<string> registered)
	{
		var names = registered.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return aliases
			.Where(pair => names.Contains(pair.Key))
			.SelectMany(pair => pair.Value.Select(alias => (Alias: alias, Target: pair.Key)))
			.Where(pair => !names.Contains(pair.Alias));
	}

	private static JsonObject Section(IEnumerable<(string Name, string? Help, JsonObject Entry)> entries)
	{
		var section = new JsonObject();
		foreach (var (name, help, entry) in entries
							 .DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
							 .OrderBy(e => e.Name, StringComparer.Ordinal))
		{
			var full = WithoutHeader((help ?? string.Empty).Replace("\r\n", "\n")).Trim();
			var ordered = new JsonObject
			{
				["helpFull"] = full,
				["helpPreview"] = string.Join('\n', full.Split('\n')
					.Select(line => line.Trim())
					.Where(line => line.Length > 0)
					.Take(PreviewLines))
			};
			foreach (var (key, value) in entry.ToList())
			{
				entry.Remove(key);
				ordered[key] = value;
			}

			section[name] = ordered;
		}

		return section;
	}

	private static IEnumerable<string> Names(IEnumerable<string> names)
		=> names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

	private static JsonArray Array(IEnumerable<string> values)
		=> new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

	private static string Serialize(JsonNode node)
		=> node.ToJsonString(Output).Replace("\r\n", "\n") + "\n";
}

