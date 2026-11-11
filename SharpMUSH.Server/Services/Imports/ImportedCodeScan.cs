using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Softcode;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>
/// Reads every attribute of an imported world for what its code asks of SharpMUSH, and warns about each thing it
/// asks for that is not here: a function, a command, a command's switch, a function called with a number of
/// arguments it does not take, and an attribute read by a name nothing in the world has.
/// </summary>
/// <remarks>
/// <para>A name counts as here when SharpMUSH has it, when the dialect of the attribute calling it has it under that
/// name (<see cref="SoftcodeDialect"/>), or when the world itself provides it: an <c>@function</c>, a <c>$</c>-command
/// the line matches, an exit, a channel. Functions are the ones <see cref="SoftcodeSyntaxAnalyzer.Calls"/> finds.
/// Commands are read from the code that runs as commands: a <c>$</c>-command's or <c>^</c>-listen's action, STARTUP and
/// the other action attributes, an attribute <c>@trigger</c> names, and the action lists of <c>@switch</c>,
/// <c>@dolist</c> and the like.</para>
/// <para>A name SharpMUSH leaves out on purpose (<see cref="ImportLeftOut"/>) is reported with the help topic that
/// says why.</para>
/// </remarks>
public static partial class ImportedCodeScan
{
	/// <summary>How many names each kind of warning lists one by one, the most used first.</summary>
	public const int MaxReported = 50;

	/// <summary>How many places a warning names for one name.</summary>
	public const int MaxPlaces = 3;

	/// <summary>The help topic that lists what SharpMUSH leaves out on purpose.</summary>
	public const string LeftOutTopic = "compatibility left out";

	[GeneratedRegex(@"@function(?:/\w+)*\s+([0-9A-Za-z_~@`]+)\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex FunctionRegistration();

	/// <summary>A name written plainly, which the scan can read without running the code around it.</summary>
	[GeneratedRegex(@"^[A-Za-z0-9_`.\-~@#$^&*+?<>|'!]+$")]
	private static partial Regex LiteralName();

	public static async Task ReportAsync(IMediator mediator, LibraryService<string, FunctionDefinition> functions,
		LibraryService<string, CommandDefinition> commands, SoftcodeSyntaxAnalyzer analyzer, string? gameDialect,
		ConversionResult result, CancellationToken ct)
	{
		var scan = new Scan(mediator, functions, commands, analyzer, ct);
		await scan.ReadWorldAsync(gameDialect);
		await scan.ReadTriggeredAsync(gameDialect);
		await scan.ReportAsync(result);
	}

	/// <summary>Where something was found, the most used first, and how often.</summary>
	private sealed class Tally
	{
		public int Count;
		public readonly List<string> Places = [];
		public readonly List<string> Samples = [];

		public void Add(string place, string? sample = null)
		{
			Count++;
			if (Places.Count < MaxPlaces && !Places.Contains(place)) Places.Add(place);
			if (sample is not null && Samples.Count < MaxPlaces) Samples.Add(sample);
		}

		public string Where => string.Join(", ", Places);
	}

	private sealed class Scan(IMediator mediator, LibraryService<string, FunctionDefinition> functions,
		LibraryService<string, CommandDefinition> commandLibrary, SoftcodeSyntaxAnalyzer analyzer, CancellationToken ct)
	{
		private readonly CommandTrie _trie = CommandTrie.For(commandLibrary);
		private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<Regex> _commandPatterns = [];
		private readonly HashSet<string> _exits = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _attributesHeld = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _attributesSet = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<SharpAttributeEntry> _standardAttributes = [];
		private readonly HashSet<(int Object, string Attribute)> _commandAttributes = [];
		private readonly Dictionary<(int Object, string Attribute), string> _triggered = [];

		private readonly Dictionary<string, Tally> _missingFunctions = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Tally> _missingCommands = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Tally> _missingSwitches = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<(string Name, int Count), Tally> _arity = [];
		private readonly Dictionary<string, SharpFunctionAttribute> _arityTakes = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Tally> _attributesRead = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _dialectOfName = new(StringComparer.OrdinalIgnoreCase);

		public async Task ReadWorldAsync(string? gameDialect)
		{
			await foreach (var entry in mediator.CreateStream(new GetAllAttributeEntriesQuery(), ct))
				_standardAttributes.Add(entry);

			await foreach (var obj in mediator.CreateStream(new GetAllObjectsQuery(), ct))
			{
				if (obj.Type == "EXIT") await ReadExitAsync(obj);

				await foreach (var match in mediator.CreateStream(new GetAttributesQuery(obj.DBRef, ".*", false,
					IAttributeService.AttributePatternMode.Regex), ct))
				{
					var attribute = match.Attribute;
					_attributesHeld.Add(attribute.LongName ?? attribute.Name);
					ReadAttribute(obj.DBRef.Number, attribute.LongName ?? attribute.Name, attribute.Value.ToPlainText(),
						Dialect(attribute, gameDialect), attribute.Flags.Any(flag => flag.Name.Equals("REGEXP", StringComparison.OrdinalIgnoreCase)));
				}
			}
		}

		/// <summary>The attributes <c>@trigger</c> names that no other rule read as commands.</summary>
		public async Task ReadTriggeredAsync(string? gameDialect)
		{
			foreach (var ((number, name), _) in _triggered.Where(pair => !_commandAttributes.Contains(pair.Key)).ToList())
			{
				SharpAttribute? found = null;
				await foreach (var attribute in mediator.CreateStream(new GetAttributeQuery(new DBRef(number), name.Split('`')), ct))
					found = attribute;
				if (found is null) continue;
				_commandAttributes.Add((number, name));
				ScanCommandList(found.Value.ToPlainText(), Dialect(found, gameDialect), Place(number, name), 0);
			}
		}

		private static SoftcodeDialect? Dialect(SharpAttribute attribute, string? gameDialect)
			=> (SoftcodeDialects.Of(attribute) ?? gameDialect) is { Length: > 0 } key
				? SoftcodeDialects.All.GetValueOrDefault(key)
				: null;

		private static string Place(int number, string attribute) => $"#{number}/{attribute}";

		private async Task ReadExitAsync(SharpObject obj)
		{
			foreach (var name in obj.Name.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				_exits.Add(name);
			if (await mediator.Send(new GetObjectNodeQuery(obj.DBRef), ct) is AnySharpObject and SharpExit { Aliases: { } aliases })
				foreach (var alias in aliases)
					_exits.Add(alias);
		}

		private void ReadAttribute(int number, string name, string text, SoftcodeDialect? dialect, bool regexp)
		{
			var place = Place(number, name);
			foreach (Match registration in FunctionRegistration().Matches(text))
				_registered.Add(registration.Groups[1].Value);

			ScanCalls(text, dialect, place);

			// $pattern:action and ^pattern:action: the pattern is matched, the action runs as commands.
			var sigil = text.Length > 0 ? text[0] : '\0';
			if ((sigil == '$' ? CommandDiscoveryService.CommandPatternRegex() : sigil == '^' ? CommandDiscoveryService.ListenPatternRegex() : null)
					?.Match(text) is { Success: true } pattern)
			{
				if (sigil == '$') AddCommandPattern(CommandDiscoveryService.UnescapePatternSeparator(pattern.Groups["pattern"].Value), regexp);
				_commandAttributes.Add((number, name));
				ScanCommandList(text[pattern.Length..], dialect, place, 0);
				return;
			}

			if (IsActionAttribute(name))
			{
				_commandAttributes.Add((number, name));
				ScanCommandList(text, dialect, place, 0);
			}
		}

		private void AddCommandPattern(string pattern, bool regexp)
		{
			try
			{
				_commandPatterns.Add(regexp
					? SoftcodeRegex.Create(pattern, RegexOptions.IgnoreCase)
					: SoftcodeRegex.Wildcard(pattern));
			}
			catch (ArgumentException)
			{
				// A pattern that does not compile matches nothing at run time either.
			}
		}

		/// <summary>STARTUP and the attribute a standard attribute's action half is (<c>ASUCC</c> for <c>SUCC</c>).</summary>
		private bool IsActionAttribute(string name)
			=> name.Equals("STARTUP", StringComparison.OrdinalIgnoreCase)
				|| name is ['A' or 'a', .. var rest] && rest.Length > 1
					&& _standardAttributes.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
					&& (_standardAttributes.Any(entry => entry.Name.Equals(rest, StringComparison.OrdinalIgnoreCase))
						|| ActionOnly.Contains(name));

		/// <summary>Action attributes whose name is not <c>A</c> before another standard attribute's.</summary>
		private static readonly HashSet<string> ActionOnly = new(StringComparer.OrdinalIgnoreCase)
		{
			"ACONNECT", "ADISCONNECT", "AHEAR", "AMHEAR", "AAHEAR", "ADESTROY", "AZENTER", "AZLEAVE"
		};

		private void ScanCalls(string text, SoftcodeDialect? dialect, string place)
		{
			foreach (var call in analyzer.Calls(MarkupText.Plain(text)))
			{
				if (Resolve(call.Name, dialect) is not { } definition)
				{
					Count(_missingFunctions, call.Name, place);
					if (dialect is not null) _dialectOfName.TryAdd("f:" + call.Name, dialect.Key);
					continue;
				}

				CheckArity(call, definition, dialect, place);
				ReadAttributeReference(call, definition.Attribute.Name, place);
			}
		}

		private FunctionDefinition? Resolve(string name, SoftcodeDialect? dialect)
		{
			if (functions.TryGetValue(name, out var found) || functions.TryGetValue(name.ToLowerInvariant(), out found))
				return found.LibraryInformation;
			return dialect?.FunctionFor(name) is { } meant
				&& (functions.TryGetValue(meant, out found) || functions.TryGetValue(meant.ToLowerInvariant(), out found))
					? found.LibraryInformation
					: null;
		}

		private void CheckArity(SoftcodeCall call, FunctionDefinition definition, SoftcodeDialect? dialect, string place)
		{
			var attribute = definition.Attribute;
			var count = call.Empty && attribute.MinArgs == 0 ? 0 : call.Arguments.Count;
			if (count > 1 && dialect?.AbsorbsCommas(attribute.Name) == true) count = 1;
			if (FunctionDispatcher.ValidateArgumentCount(attribute, call.Name, count, call.Empty) is null) return;
			Count(_arity, (call.Name.ToLowerInvariant(), count), place);
			_arityTakes.TryAdd(call.Name, attribute);
		}

		/// <summary>
		/// An attribute a call reads or sets by a name written plainly: <c>u(me/DO_IT)</c>, <c>get(%#/RACE)</c>,
		/// <c>v(STAT)</c>, <c>set(me,LAST:...)</c>.
		/// </summary>
		private void ReadAttributeReference(SoftcodeCall call, string function, string place)
		{
			var arguments = call.Arguments;
			switch (function.ToUpperInvariant())
			{
				case "U" or "UFUN" or "ULOCAL" or "GET" or "GET_EVAL" or "UDEFAULT" when arguments.Count > 0:
					if (AttributePart(arguments[0]) is { } named && function.ToUpperInvariant() is not "UDEFAULT")
						Count(_attributesRead, named, place);
					break;
				case "XGET" or "EVAL" when arguments.Count > 1:
					if (Plain(arguments[1]) is { } attribute) Count(_attributesRead, attribute, place);
					break;
				case "V" when arguments.Count == 1:
					if (Plain(arguments[0]) is { Length: > 1 } held && !held.All(char.IsDigit)) Count(_attributesRead, held, place);
					break;
				case "SET" when arguments.Count > 1:
					if (arguments[1].IndexOf(':') is > 0 and var colon && Plain(arguments[1][..colon]) is { } written)
						_attributesSet.Add(written);
					break;
				case "ATTRIB_SET" or "WIPE" when arguments.Count > 0:
					if (AttributePart(arguments[0]) is { } setName) _attributesSet.Add(setName);
					break;
			}
		}

		/// <summary>The attribute of <c>object/attribute</c>, or the whole text when it names no object.</summary>
		private static string? AttributePart(string text)
		{
			var trimmed = text.Trim();
			if (trimmed.StartsWith("#lambda", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("#apply", StringComparison.OrdinalIgnoreCase))
				return null;
			var slash = trimmed.IndexOf('/');
			return Plain(slash < 0 ? trimmed : trimmed[(slash + 1)..]);
		}

		private static string? Plain(string text)
			=> text.Trim() is { Length: > 0 } trimmed && LiteralName().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : null;

		private void ScanCommandList(string text, SoftcodeDialect? dialect, string place, int depth)
		{
			if (depth > 8 || string.IsNullOrWhiteSpace(text)) return;
			foreach (var command in analyzer.Commands(MarkupText.Plain(text)))
				ScanCommand(command, dialect, place, depth);
		}

		private void ScanCommand(string line, SoftcodeDialect? dialect, string place, int depth)
		{
			if (line.Length == 0) return;
			switch (line[0])
			{
				case '{':
					ScanCommandList(Unbrace(line), dialect, place, depth + 1);
					return;
				case '}' when dialect == SoftcodeDialects.Rhost:
					ScanCommand(line[1..].TrimStart(), dialect, place, depth);
					return;
				case '&':
					if (Plain(line[1..].Split(' ', 2)[0]) is { } written) _attributesSet.Add(written);
					return;
				// Speech, the force prefix, and a line that starts with code: nothing to name.
				case '"' or ':' or ';' or '\\' or '\'' or '[' or '%' or '(' or '#' or '-' or '|':
					return;
			}

			var word = line.Split(' ', 2)[0];
			var equals = word.IndexOf('=');
			if (equals > 0) word = word[..equals];
			// A name the code computes is not one the scan can read.
			if (word.Length == 0 || word.IndexOfAny(['[', '%', '(', '{', '\\']) >= 0) return;

			var parts = word.Split('/');
			var root = parts[0];
			var switches = parts[1..].Where(part => part.Length > 0).ToArray();
			if (root.Length == 0) return;

			if (dialect?.Lacks(root) is true)
			{
				CountCommand(root, dialect, place, line);
				return;
			}

			if ((_trie.FindExact(root) is null && dialect?.CommandFor(root) is { } meant
					? _trie.FindExact(meant.Split('/')[0])
					: _trie.FindAbbreviation(root)?.Definition) is not { } definition)
			{
				if (!IsAttributeCommand(root, switches, dialect)) CountCommand(root, dialect, place, line);
				return;
			}

			foreach (var unknown in switches.Where(typed => !Knows(definition, typed, dialect)))
				Count(_missingSwitches, $"{definition.Attribute.Name.ToLowerInvariant()}/{unknown.ToLowerInvariant()}", place);

			if (definition.Attribute.Name is "@TRIGGER" or "@INCLUDE")
				ReadTriggerTarget(line[word.Length..], place);
			if (definition.Attribute.Name is "@SET")
				ReadSetTarget(line[word.Length..]);

			foreach (var actions in ActionLists(definition.Attribute.Name, line[word.Length..]))
				ScanCommandList(actions, dialect, place, depth + 1);
		}

		private void CountCommand(string root, SoftcodeDialect? dialect, string place, string line)
		{
			Count(_missingCommands, root.ToLowerInvariant(), place, line);
			if (dialect is not null) _dialectOfName.TryAdd("c:" + root.ToLowerInvariant(), dialect.Key);
		}

		private static bool Knows(CommandDefinition definition, string typed, SoftcodeDialect? dialect)
		{
			var known = definition.Attribute.Switches ?? [];
			return known.Contains("*") || known.Contains(typed, StringComparer.OrdinalIgnoreCase)
				|| dialect?.SwitchFor(definition.Attribute.Name, typed) is not null;
		}

		/// <summary><c>@desc me=...</c>: a standard attribute's own command, or one the dialect names an attribute by.</summary>
		private bool IsAttributeCommand(string root, string[] switches, SoftcodeDialect? dialect)
		{
			if (root.Length < 2 || root[0] != '@' || switches.Length > 0) return false;
			var name = dialect?.AttributeFor(root[1..]) ?? root[1..];
			return _standardAttributes.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
				|| (entry.DefaultFlags.Contains("prefixmatch", StringComparer.OrdinalIgnoreCase)
					&& entry.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)));
		}

		/// <summary><c>@trigger me/DO_IT</c>: DO_IT runs as commands, and is read as an attribute.</summary>
		private void ReadTriggerTarget(string arguments, string place)
		{
			var target = TopLevelSplit(arguments, '=')[0].Trim();
			var slash = target.IndexOf('/');
			if (slash <= 0 || Plain(target[(slash + 1)..]) is not { } name) return;
			Count(_attributesRead, name, place);
			var number = target[..slash].Trim() switch
			{
				"me" or "%!" or "ME" => int.Parse(place[1..place.IndexOf('/')]),
				['#', .. var digits] when int.TryParse(digits, out var parsed) => parsed,
				_ => -1
			};
			if (number >= 0) _triggered.TryAdd((number, name), place);
		}

		/// <summary><c>@set obj=NAME:value</c> sets NAME.</summary>
		private void ReadSetTarget(string arguments)
		{
			var pieces = TopLevelSplit(arguments, '=');
			if (pieces.Count < 2 || pieces[1].IndexOf(':') is not (> 0 and var colon)) return;
			if (Plain(pieces[1][..colon]) is { } name) _attributesSet.Add(name);
		}

		/// <summary>
		/// The parts of a command's arguments that run as commands: every action of <c>@switch</c>, <c>@select</c>,
		/// <c>@ifelse</c> and <c>@skip</c>, and the whole right side of <c>@dolist</c>, <c>@wait</c>, <c>@force</c>,
		/// <c>@break</c>, <c>@assert</c>, <c>@map</c> and <c>@mapsql</c>.
		/// </summary>
		private static IEnumerable<string> ActionLists(string command, string arguments)
		{
			var sides = TopLevelSplit(arguments, '=', 2);
			if (sides.Count < 2) yield break;
			var right = sides[1];
			switch (command)
			{
				case "@SWITCH" or "@SELECT":
					var cases = TopLevelSplit(right, ',');
					for (var i = 1; i < cases.Count; i += 2) yield return Unbrace(cases[i]);
					if (cases.Count % 2 == 1 && cases.Count > 1) yield return Unbrace(cases[^1]);
					break;
				case "@IFELSE" or "@SKIP":
					foreach (var action in TopLevelSplit(right, ',')) yield return Unbrace(action);
					break;
				case "@DOLIST" or "@WAIT" or "@FORCE" or "@BREAK" or "@ASSERT" or "@MAP" or "@MAPSQL":
					yield return Unbrace(right);
					break;
			}
		}

		/// <summary>The text inside one pair of braces that wraps all of it, or the text as it is.</summary>
		private static string Unbrace(string text)
		{
			var trimmed = text.Trim();
			return trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[^1] == '}' && TopLevelSplit(trimmed[1..^1], '}').Count == 1
				? trimmed[1..^1]
				: trimmed;
		}

		/// <summary><paramref name="text"/> split at each <paramref name="separator"/> outside braces, brackets and parentheses.</summary>
		private static List<string> TopLevelSplit(string text, char separator, int limit = int.MaxValue)
		{
			var pieces = new List<string>();
			var depth = 0;
			var start = 0;
			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];
				if (c == '\\') { i++; continue; }
				if (c is '{' or '[' or '(') depth++;
				else if (c is '}' or ']' or ')') depth = Math.Max(0, depth - 1);
				else if (c == separator && depth == 0 && pieces.Count < limit - 1)
				{
					pieces.Add(text[start..i]);
					start = i + 1;
				}
			}

			pieces.Add(text[start..]);
			return pieces;
		}

		private static void Count<TKey>(Dictionary<TKey, Tally> tallies, TKey key, string place, string? sample = null)
			where TKey : notnull
		{
			if (!tallies.TryGetValue(key, out var tally)) tallies[key] = tally = new Tally();
			tally.Add(place, sample);
		}

		/// <summary>Whether something in the world answers <paramref name="name"/> typed as a command.</summary>
		private bool WorldAnswers(string name, Tally tally, IReadOnlyList<string> channels)
			=> _exits.Contains(name)
				|| (name.StartsWith('+') && name.Length > 1
					&& channels.Any(channel => channel.StartsWith(name[1..], StringComparison.OrdinalIgnoreCase)))
				|| tally.Samples.Any(sample => _commandPatterns.Any(pattern => SoftcodeRegex.IsMatch(pattern, sample.Trim())))
				|| _commandPatterns.Any(pattern => SoftcodeRegex.IsMatch(pattern, name));

		public async Task ReportAsync(ConversionResult result)
		{
			var channels = new List<string>();
			await foreach (var channel in mediator.CreateStream(new GetChannelListQuery(), ct))
				channels.Add(channel.Name.ToPlainText());

			Report(result, _missingFunctions.Where(pair => !_registered.Contains(pair.Key)),
				(name, tally) => $"Softcode: {name.ToLowerInvariant()}() is called {Times(tally.Count)}, and SharpMUSH has no function by that name."
					+ LeftOut('f', name) + $" First in {tally.Where}.",
				rest => $"Softcode: {rest.Count} more functions SharpMUSH does not have are called: "
					+ string.Join(", ", rest.Select(name => name.ToLowerInvariant() + "()")) + ".");

			Report(result, _missingCommands.Where(pair => !WorldAnswers(pair.Key, pair.Value, channels)),
				(name, tally) => $"Softcode: the command {name} is run {Times(tally.Count)}, and neither SharpMUSH nor anything in the world answers it."
					+ LeftOut('c', name) + $" First in {tally.Where}.",
				rest => $"Softcode: {rest.Count} more commands nothing answers are run: {string.Join(", ", rest)}.");

			Report(result, _missingSwitches,
				(name, tally) => $"Softcode: {name} is run {Times(tally.Count)}, and {name[..name.IndexOf('/')]} has no /{name[(name.IndexOf('/') + 1)..]} switch. First in {tally.Where}.",
				rest => $"Softcode: {rest.Count} more switches SharpMUSH's commands do not have are used: {string.Join(", ", rest)}.");

			Report(result, _arity.Where(pair => !_registered.Contains(pair.Key.Name))
					.Select(pair => KeyValuePair.Create($"{pair.Key.Name}()|{pair.Key.Count}", pair.Value)),
				(key, tally) => ArityText(key, tally),
				rest => $"Softcode: {rest.Count} more calls give a function a number of arguments it does not take: "
					+ string.Join(", ", rest.Select(key => key.Replace("|", " with "))) + ".");

			var known = new HashSet<string>(_attributesHeld, StringComparer.OrdinalIgnoreCase);
			known.UnionWith(_attributesSet);
			known.UnionWith(_standardAttributes.Select(entry => entry.Name));
			known.UnionWith(AttributeService.StandardAttributeAliases.Keys);
			Report(result, _attributesRead.Where(pair => !known.Contains(pair.Key) && !AliasHeld(pair.Key, known)),
				(name, tally) => $"Softcode: the attribute {name} is read {Times(tally.Count)}, and no object in the world has it and no code sets it. First in {tally.Where}.",
				rest => $"Softcode: {rest.Count} more attributes are read that no object has and no code sets: {string.Join(", ", rest)}.");
		}

		/// <summary>An attribute a dialect reads under another name, held under the name it means here.</summary>
		private static bool AliasHeld(string name, HashSet<string> known)
			=> SoftcodeDialects.All.Values.Any(dialect => dialect.AttributeFor(name) is { } meant && known.Contains(meant));

		private string ArityText(string key, Tally tally)
		{
			var (name, count) = (key[..key.IndexOf('|')], int.Parse(key[(key.IndexOf('|') + 1)..]));
			var attribute = _arityTakes[name[..^2]];
			return $"Softcode: {name} is called with {Arguments(count)} {Times(tally.Count)}, and it takes "
				+ $"{Takes(attribute.MinArgs, attribute.MaxArgs)}. First in {tally.Where}.";
		}

		private static string Takes(int min, int max) => (min, max) switch
		{
			_ when min == max => Arguments(min),
			(_, int.MaxValue) => $"at least {Arguments(min)}",
			(0, _) => $"at most {Arguments(max)}",
			_ => $"{min} to {max} arguments"
		};

		private static string Arguments(int count) => count == 1 ? "1 argument" : $"{count} arguments";

		/// <summary>The sentence that names the help topic, when SharpMUSH leaves the name out on purpose.</summary>
		private string LeftOut(char kind, string name)
		{
			var dialect = _dialectOfName.GetValueOrDefault($"{kind}:{name.ToLowerInvariant()}");
			return (kind == 'f' ? ImportLeftOut.LeavesOutFunction(dialect, name) : ImportLeftOut.LeavesOutCommand(dialect, name))
				? $" SharpMUSH leaves it out on purpose; see 'help {LeftOutTopic}'."
				: "";
		}

		private static void Report<TKey>(ConversionResult result, IEnumerable<KeyValuePair<TKey, Tally>> found,
			Func<TKey, Tally, string> one, Func<List<TKey>, string> more) where TKey : notnull
		{
			var ordered = found.OrderByDescending(pair => pair.Value.Count)
				.ThenBy(pair => pair.Key.ToString() ?? "", StringComparer.OrdinalIgnoreCase).ToList();
			foreach (var pair in ordered.Take(MaxReported))
				result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode, one(pair.Key, pair.Value), NeedsAction: true));
			if (ordered.Count > MaxReported)
				result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode,
					more(ordered.Skip(MaxReported).Select(pair => pair.Key).ToList()), NeedsAction: true));
		}

		private static string Times(int count) => count == 1 ? "once" : $"{count} times";
	}
}
