using System.Text;
using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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
/// arguments it does not take, and an attribute called as code (<c>u()</c>, <c>@trigger</c>) by a name nothing in the world has.
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

	/// <summary>
	/// An <c>@function</c> whose name the code computes: <c>@function [rest(##,.)]=%!/##</c>, <c>@function ##=me/##</c>,
	/// <c>@function %0=...</c>.
	/// </summary>
	[GeneratedRegex(@"@function(?:/\w+)*\s+(?=[^=;]*(?:\[|##|%))", RegexOptions.IgnoreCase)]
	private static partial Regex ComputedRegistration();

	/// <summary>
	/// <c>@dolist a b c={@function ##=me/fn_##}</c>: the names are the list's words, when it is written plainly.
	/// </summary>
	[GeneratedRegex(@"@dolist(?:/\w+)*\s+([^=\[\]%{}();]+?)\s*=\s*\{?\s*@function(?:/\w+)*\s+##\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex ListedRegistrations();

	/// <summary>The attribute an <c>@function</c> runs: <c>@function name=obj,attr</c> or <c>obj/attr</c>.</summary>
	[GeneratedRegex(@"@function(?:/\w+)*\s+[^=;]+=\s*[^,/;=}\]]+[,/]\s*([A-Za-z0-9_`.\-~@#$^&*+?<>|'!]+)", RegexOptions.IgnoreCase)]
	private static partial Regex RegisteredAttribute();

	/// <summary>An attribute that holds a SQL statement, which sql(u(...)) and the like read as text for the database.</summary>
	[GeneratedRegex(@"^\s*(?:SELECT\s.*\sFROM\s|(?:INSERT|REPLACE)\s+INTO\s|UPDATE\s+\S+\s+SET\s|DELETE\s+FROM\s|(?:CREATE|ALTER|DROP)\s+(?:TABLE|INDEX|VIEW)\s|CALL\s+\w+\s*\()", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex SqlStatement();

	/// <summary>
	/// An objid built from whole seconds, <c>[num(%0)]:[csecs(%0)]</c>: PennMUSH's form. It names an object the import
	/// brought over, whose creation time is whole seconds, but not one created here.
	/// </summary>
	[GeneratedRegex(@"(?:\]|%[0-9#!@]|##|#\d+):\[csecs\([^,\]]*\)\]", RegexOptions.IgnoreCase)]
	private static partial Regex SecondsObjid();

	/// <summary>
	/// A bell given as the delimiter of a list function that takes colour off its arguments, and so the bell too:
	/// <c>words(%0,beep())</c>.
	/// </summary>
	[GeneratedRegex(@"\b(?:words|items|member|match|matchall|wordpos|listset|lnum|namegrab|nummember|totmember)\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*,\s*(?:\[beep\(\)\]|beep\(\))\s*[,)]", RegexOptions.IgnoreCase)]
	private static partial Regex BellArgument();

	/// <summary>
	/// <c>@dolist lattr(%!/ufunc.*)=@function [rest(##,.)]=%!/##</c>: every attribute the pattern lists runs, so the
	/// pattern's written start (UFUNC.) names them.
	/// </summary>
	[GeneratedRegex(@"@dolist(?:/\w+)*\s+\[?\s*lattr\(\s*[^/()\[\]]*/\s*([^*?()\[\]%,\s]+)[*?]", RegexOptions.IgnoreCase)]
	private static partial Regex ListedAttributes();

	/// <summary>The attribute a hook runs: <c>@hook/before cmd=obj,attr</c>.</summary>
	[GeneratedRegex(@"@hook/\w+\s+[^=;]+=\s*[^,;=}\]]+,\s*([A-Za-z0-9_`.\-~@#$^&*+?<>|'!]+)", RegexOptions.IgnoreCase)]
	private static partial Regex HookedAttribute();

	/// <summary>The attribute <c>@mapsql obj/attr=query</c> runs for each row.</summary>
	[GeneratedRegex(@"@mapsql(?:/\w+)*\s+[^/=;]+/([A-Za-z0-9_`.\-~@#$^&*+?<>|'!]+)\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex MapSqlAttribute();

	/// <summary><c>@verb victim=actor,what,whatd,owhat,owhatd,awhat,args</c>: the arguments after the actor.</summary>
	[GeneratedRegex(@"@verb(?:/\w+)*\s+[^=;]+=\s*[^,;]*,([^;}]*)", RegexOptions.IgnoreCase)]
	private static partial Regex VerbArguments();

	/// <summary>The attributes an http_handler runs, one for each request method.</summary>
	private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
	{
		"GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"
	};

	/// <summary>A call at the start of a value: <c>strcat(</c>.</summary>
	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*\s*\(")]
	private static partial Regex LeadingCall();

	/// <summary>A name written plainly, which the scan can read without running the code around it.</summary>
	[GeneratedRegex(@"^[A-Za-z0-9_`.\-~@#$^&*+?<>|'!]+$")]
	private static partial Regex LiteralName();

	public static async Task ReportAsync(IMediator mediator, LibraryService<string, FunctionDefinition> functions,
		LibraryService<string, CommandDefinition> commands, SoftcodeSyntaxAnalyzer analyzer, string? gameDialect,
		char chatToken, ConversionResult result, CancellationToken ct)
	{
		var scan = new Scan(mediator, functions, commands, analyzer, chatToken, ct);
		await scan.ReadWorldAsync(gameDialect);
		await scan.ReadTriggeredAsync(gameDialect);
		scan.ReadRunData();
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
		LibraryService<string, CommandDefinition> commandLibrary, SoftcodeSyntaxAnalyzer analyzer, char chatToken, CancellationToken ct)
	{
		private readonly char _chatToken = chatToken;
		private readonly CommandTrie _trie = CommandTrie.For(commandLibrary);
		private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<string> _computedRegistrations = [];
		private readonly Tally _unparsed = new();
		private readonly Tally _secondsObjids = new();
		private readonly Tally _bellDelimiters = new();
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

		/// <summary>Attributes that are neither commands nor ones the server runs, read for calls only when code runs them.</summary>
		private readonly List<(string Name, string Text, SoftcodeDialect? Dialect, string Place)> _data = [];

		/// <summary>Attribute names code evaluates beyond u() and @trigger: map(), filter(), an @function's attribute and the like.</summary>
		private readonly HashSet<string> _evaluated = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// The written start of attribute names code builds as it runs: <c>u(me/do_[%0])</c> may run any attribute
		/// whose name starts DO_.
		/// </summary>
		private readonly HashSet<string> _evaluatedPrefixes = new(StringComparer.OrdinalIgnoreCase);

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
				// The query walks the path; only the attribute at its end is the one named.
				if (found is null || !(found.LongName ?? found.Name).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
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
			foreach (Match registration in RegisteredAttribute().Matches(text))
				if (Plain(registration.Groups[1].Value) is { } runs) _evaluated.Add(runs);
			foreach (Match listing in ListedAttributes().Matches(text))
				if (listing.Groups[1].Value.Length >= 2) _evaluatedPrefixes.Add(listing.Groups[1].Value);
			foreach (Match hook in HookedAttribute().Matches(text))
				if (Plain(hook.Groups[1].Value) is { } runs) _evaluated.Add(runs);
			foreach (Match mapsql in MapSqlAttribute().Matches(text))
				if (Plain(mapsql.Groups[1].Value) is { } runs) _evaluated.Add(runs);
			foreach (Match verb in VerbArguments().Matches(text))
				foreach (var argument in verb.Groups[1].Value.Split(',').Take(5))
					if (Plain(argument.Trim()) is { } runs) _evaluated.Add(runs);
			var listed = ListedRegistrations().Matches(text);
			foreach (Match registration in listed)
				foreach (var listedName in registration.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
					_registered.Add(listedName);
			if (_computedRegistrations.Count < MaxPlaces && ComputedRegistration().Matches(text).Count > listed.Count)
				_computedRegistrations.Add(place);

			if (SecondsObjid().IsMatch(text)) _secondsObjids.Add(place);
			if (BellArgument().IsMatch(text)) _bellDelimiters.Add(place);

			// $pattern:action and ^pattern:action: the pattern is matched, the action runs as commands.
			var sigil = text.Length > 0 ? text[0] : '\0';
			if ((sigil == '$' ? CommandDiscoveryService.CommandPatternRegex() : sigil == '^' ? CommandDiscoveryService.ListenPatternRegex() : null)
					?.Match(text) is { Success: true } pattern)
			{
				if (sigil == '$') AddCommandPattern(CommandDiscoveryService.UnescapePatternSeparator(pattern.Groups["pattern"].Value), regexp);
				ScanCalls(text, dialect, place);
				_commandAttributes.Add((number, name));
				CheckParses(text[pattern.Length..], ParseType.CommandList, place);
				ScanCommandList(text[pattern.Length..], dialect, place, 0);
				return;
			}

			if (IsActionAttribute(name))
			{
				ScanCalls(text, dialect, place);
				_commandAttributes.Add((number, name));
				CheckParses(text, ParseType.CommandList, place);
				ScanCommandList(text, dialect, place, 0);
				return;
			}

			// The server evaluates its standard attributes (DESCRIBE, SUCCESS); any other is data, help text or a SQL
			// statement until some code runs it.
			if (_standardAttributes.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
				ScanCalls(text, dialect, place);
			else if (!SqlStatement().IsMatch(text))
				_data.Add((name, text, dialect, place));

			CheckParses(text, ParseType.Function, place);
		}

		/// <summary>
		/// The calls of each data attribute some code runs: by u(), @trigger, map() and the like, or as an @function. An
		/// attribute it runs can run others, so this reads until no more are found. Code also reaches attributes in ways
		/// the scan cannot follow (an @function registered under a computed name, a name built from a register), so an
		/// attribute nothing is seen to run is read too when it is written as code: it holds <c>[ ]</c>, or it is one
		/// call from start to end (<c>strcat(...)</c>). Text such as help (<c>foo() explains it.</c>) is left alone.
		/// </summary>
		public void ReadRunData()
		{
			for (var found = true; found;)
			{
				found = false;
				for (var i = _data.Count - 1; i >= 0; i--)
				{
					var (name, text, dialect, place) = _data[i];
					if (!Runs(name)) continue;
					_data.RemoveAt(i);
					ScanCalls(text, dialect, place);
					found = true;
				}
			}

			foreach (var (_, text, dialect, place) in _data.Where(entry => WrittenAsCode(entry.Text)))
				ScanCalls(text, dialect, place);
			_data.Clear();
		}

		/// <summary>Whether <paramref name="text"/> reads as code: it holds <c>[ ]</c>, or one call spans all of it.</summary>
		private static bool WrittenAsCode(string text)
		{
			if (text.Contains('[')) return true;
			var trimmed = text.Trim();
			if (LeadingCall().Match(trimmed) is not { Success: true } call) return false;
			var depth = 0;
			for (var i = call.Length - 1; i < trimmed.Length; i++)
			{
				var c = trimmed[i];
				if (c == '\\') i++;
				else if (c == '(') depth++;
				else if (c == ')' && --depth == 0) return i == trimmed.Length - 1;
			}

			return false;
		}

		/// <summary>
		/// Whether some code runs attribute <paramref name="name"/>: a call or command names it to run it, or it is an
		/// event the event handler hears or a request method the http handler answers.
		/// </summary>
		private bool Runs(string name)
			=> _attributesRead.ContainsKey(name) || _evaluated.Contains(name)
				|| _evaluatedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				|| EventCatalogue.Find(name) is not null || HttpMethods.Contains(name);

		/// <summary>
		/// Code whose brackets, braces or parentheses do not pair the way SharpMUSH reads them fails each time it runs
		/// (<c>#-1 PARSER FAILURE</c>). Which closer was meant cannot be guessed, so it is reported, not rewritten.
		/// </summary>
		private void CheckParses(string text, ParseType parseType, string place)
		{
			if (text.AsSpan().IndexOfAny('[', '{') < 0) return;
			if (analyzer.ValidateAndGetErrors(MarkupText.Plain(text), parseType) is [var first, ..])
				_unparsed.Add(place, $"{first.Message} at position {first.Column}");
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
			var calls = analyzer.Calls(MarkupText.Plain(text));
			var sql = SqlQueryCalls(calls);
			foreach (var call in calls)
			{
				if (Resolve(call.Name, dialect) is not { } definition)
				{
					// COUNT(*) in sql()'s query is the database's function, not softcode's.
					if (sql.TryGetValue(call.Name, out var inQuery) && inQuery > 0)
					{
						sql[call.Name] = inQuery - 1;
						continue;
					}

					Count(_missingFunctions, call.Name, place);
					if (dialect is not null) _dialectOfName.TryAdd("f:" + call.Name, dialect.Key);
					continue;
				}

				CheckArity(call, definition, dialect, place);
				ReadAttributeReference(call, definition.Attribute.Name, place);
			}
		}

		/// <summary>How many calls of each name sit in the query of a sql() or mapsql() among <paramref name="calls"/>.</summary>
		private Dictionary<string, int> SqlQueryCalls(IReadOnlyList<SoftcodeCall> calls)
		{
			var inQueries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (var call in calls)
			{
				var query = call.Name.ToUpperInvariant() switch
				{
					"SQL" when call.Arguments.Count > 0 => call.Arguments[0],
					"MAPSQL" when call.Arguments.Count > 1 => call.Arguments[1],
					_ => null
				};
				if (query is null || query.IndexOf('(') < 0) continue;
				// Calls in [ ] inside the query are softcode's, evaluated before the database sees it.
				foreach (var inner in analyzer.Calls(MarkupText.Plain($"[{OutsideBrackets(query)}]")))
					inQueries[inner.Name] = inQueries.GetValueOrDefault(inner.Name) + 1;
			}

			return inQueries;
		}

		/// <summary><paramref name="text"/> without its <c>[ ]</c> spans, nested ones and all.</summary>
		private static string OutsideBrackets(string text)
		{
			var builder = new StringBuilder(text.Length);
			var depth = 0;
			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];
				if (c == '\\' && i + 1 < text.Length)
				{
					if (depth == 0) builder.Append(c).Append(text[i + 1]);
					i++;
				}
				else if (c == '[') depth++;
				else if (c == ']' && depth > 0) depth--;
				else if (depth == 0) builder.Append(c);
			}

			return builder.ToString();
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
				// Code it calls, which has to be there. Data reads (get(), v()) are left alone: code often reads data it
				// has not set yet, behind a default() or hasattr().
				case "U" or "UFUN" or "ULOCAL" when arguments.Count > 0:
					if (AttributePart(arguments[0]) is { } named) Count(_attributesRead, named, place);
					else ReadComputedName(arguments[0]);
					break;
				// Functions that run an attribute as code, so the calls in it are calls.
				case "UDEFAULT" or "ULDEFAULT" or "EDEFAULT" or "ZFUN" or "PFUN" or "GET_EVAL" or "FILTER" or "FILTERBOOL" or "MAP" or "FOLD"
					or "SORTBY" or "SORTKEY" or "MIX" or "MUNGE" or "FOREACH" or "STEP" or "MAPSQL" or "TRIGGER" or "ULAMBDA"
					or "UARGS" or "EVERY" or "SOME"
					when arguments.Count > 0:
					if (AttributePart(arguments[0]) is { } run) _evaluated.Add(run);
					else ReadComputedName(arguments[0]);
					break;
				case "EVAL" when arguments.Count > 1:
					if (Plain(arguments[1]) is { } evaluated) _evaluated.Add(evaluated);
					else ReadComputedName(arguments[1]);
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

		/// <summary>
		/// The written start of an attribute name built at run time (<c>me/do_[%0]</c> starts DO_), kept when it is long
		/// enough to tell attributes apart.
		/// </summary>
		private void ReadComputedName(string text)
		{
			var trimmed = text.Trim();
			if (trimmed.StartsWith("#lambda", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("#apply", StringComparison.OrdinalIgnoreCase))
				return;
			var start = 0;
			for (int i = 0, depth = 0; i < trimmed.Length; i++)
			{
				if (trimmed[i] is '[' or '(' or '{') depth++;
				else if (trimmed[i] is ']' or ')' or '}' && depth > 0) depth--;
				else if (trimmed[i] == '/' && depth == 0)
				{
					start = i + 1;
					break;
				}
			}

			var end = trimmed.IndexOfAny(['[', '%', '#', '(', '{', '\\'], start);
			if (end < 0) return;
			var prefix = trimmed[start..end];
			if (prefix.Length >= 2 && LiteralName().IsMatch(prefix)) _evaluatedPrefixes.Add(prefix);
		}

		/// <summary>The name, upper-cased, when it is written plainly; an iter() token in it (<c>##</c>) makes it computed.</summary>
		private static string? Plain(string text)
			=> text.Trim() is { Length: > 0 } trimmed && LiteralName().IsMatch(trimmed)
				&& !IterTokens.Any(token => trimmed.Contains(token, StringComparison.Ordinal))
					? trimmed.ToUpperInvariant()
					: null;

		private static readonly string[] IterTokens = ["##", "#@", "#$", "#+", "#!"];

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
				// RhostMUSH's } keeps spaces, and ] runs the command unevaluated: either way the command follows.
				case '}' when dialect == SoftcodeDialects.Rhost:
				case ']':
					ScanCommand(line[1..].TrimStart(), dialect, place, depth);
					return;
				case '&':
					if (Plain(line[1..].Split(' ', 2)[0]) is { } written) _attributesSet.Add(written);
					return;
				// Speech, the force prefix, and a line that starts with code: nothing to name.
				case '"' or ':' or ';' or '\\' or '\'' or '[' or '%' or '(' or '#' or '-' or '|' or '~':
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

			if (_trie.FindExact(root) is null && dialect?.Lacks(root) is true)
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

			foreach (var unknown in switches.Where(typed => !Knows(definition, root, typed, dialect)))
			{
				var key = $"{definition.Attribute.Name.ToLowerInvariant()}/{unknown.ToLowerInvariant()}";
				Count(_missingSwitches, key, place);
				if (dialect is not null) _dialectOfName.TryAdd("s:" + key, dialect.Key);
			}

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

		private static bool Knows(CommandDefinition definition, string root, string typed, SoftcodeDialect? dialect)
		{
			var known = definition.Attribute.Switches ?? [];
			return known.Contains("*") || known.Contains(typed, StringComparer.OrdinalIgnoreCase)
				|| dialect?.SwitchMeaning(definition.Attribute.Name, known, typed, root) is not null;
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
			if (slash <= 0) return;
			if (Plain(target[(slash + 1)..]) is not { } name)
			{
				ReadComputedName(target);
				return;
			}

			Count(_attributesRead, name, place);
			var number = target[..slash].Trim() switch
			{
				var self when self.Equals("me", StringComparison.OrdinalIgnoreCase) || self == "%!" => int.Parse(place[1..place.IndexOf('/')]),
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
		/// <c>@ifelse</c> and <c>@skip</c>, and the whole right side of <c>@dolist</c>, <c>@wait</c>,
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
				// @force is left out: the forced object runs it, with its own exits, aliases and $-commands in reach.
				case "@DOLIST" or "@WAIT" or "@BREAK" or "@ASSERT" or "@MAP" or "@MAPSQL":
					yield return Unbrace(right);
					break;
			}
		}

		/// <summary>The text inside one pair of braces that wraps all of it, or the text as it is.</summary>
		private static string Unbrace(string text)
		{
			var trimmed = text.Trim();
			return trimmed.Length >= 2 && trimmed[0] == '{' && ClosingBrace(trimmed) == trimmed.Length - 1
				? trimmed[1..^1]
				: trimmed;
		}

		/// <summary>Where the brace that opens <paramref name="text"/> closes, or -1.</summary>
		private static int ClosingBrace(string text)
		{
			var depth = 0;
			for (var i = 0; i < text.Length; i++)
			{
				switch (text[i])
				{
					case '\\':
						i++;
						break;
					case '{':
						depth++;
						break;
					case '}' when --depth == 0:
						return i;
				}
			}

			return -1;
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
			=> ct.IsCancellationRequested ? throw new OperationCanceledException(ct) : _exits.Contains(name)
				|| (name[0] is '+' || name[0] == _chatToken) && name.Length > 1
					&& channels.Any(channel => channel.StartsWith(name[1..], StringComparison.OrdinalIgnoreCase))
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
					+ Listed([.. rest.Select(name => name.ToLowerInvariant() + "()")]) + ".");
			if (_computedRegistrations.Count > 0 && _missingFunctions.Keys.Any(name => !_registered.Contains(name)))
				result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode,
					$"Softcode: {string.Join(", ", _computedRegistrations)} registers @functions under names it computes, "
					+ "so some of the functions above may be there once that code runs.", NeedsAction: true));

			Report(result, _missingCommands.Where(pair => !WorldAnswers(pair.Key, pair.Value, channels)),
				(name, tally) => $"Softcode: the command {name} is run {Times(tally.Count)}, and neither SharpMUSH nor anything in the world answers it."
					+ LeftOut('c', name) + $" First in {tally.Where}.",
				rest => $"Softcode: {rest.Count} more commands nothing answers are run: {Listed(rest)}.");

			Report(result, _missingSwitches,
				(name, tally) => $"Softcode: {name} is run {Times(tally.Count)}, and {name[..name.IndexOf('/')]} has no /{name[(name.IndexOf('/') + 1)..]} switch."
					+ LeftOut('s', name) + $" First in {tally.Where}.",
				rest => $"Softcode: {rest.Count} more switches SharpMUSH's commands do not have are used: {Listed(rest)}.");

			Report(result, _arity.Where(pair => !_registered.Contains(pair.Key.Name))
					.Select(pair => KeyValuePair.Create($"{pair.Key.Name}()|{pair.Key.Count}", pair.Value)),
				(key, tally) => ArityText(key, tally),
				rest => $"Softcode: {rest.Count} more calls give a function a number of arguments it does not take: "
					+ Listed([.. rest.Select(key => key.Replace("|", " with "))]) + ".");

			if (_unparsed.Count > 0)
				result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode,
					$"Softcode: {_unparsed.Count} attribute(s) do not parse, so they fail with #-1 PARSER FAILURE when they run. "
					+ "Inside [ ], a ] in a function's argument or in braces closes the [ ], and the import cannot tell which one was meant; "
					+ $"escape it as \\]. First in {string.Join("; ", _unparsed.Places.Zip(_unparsed.Samples, (where, why) => $"{where} ({why})"))}.",
					NeedsAction: true));

			if (_secondsObjids.Count > 0)
				result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode,
					$"Softcode: {_secondsObjids.Count} attribute(s) build an objid as <dbref>:[csecs(<object>)]. That names the objects "
					+ "the import brought over, but not objects created here, whose objids are in milliseconds; use objid(<object>), "
					+ $"or csecs(<object>,ms). First in {_secondsObjids.Where}.",
					NeedsAction: true));

			if (_bellDelimiters.Count > 0)
				result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode,
					$"Softcode: {_bellDelimiters.Count} attribute(s) give beep() as the delimiter of a function that takes colour off its "
					+ "arguments, such as words() or member(), which takes the bell off too; a marked separator "
					+ $"(%*<|>, see help list separators) splits the same way everywhere. First in {_bellDelimiters.Where}.",
					NeedsAction: true));

			var known = new HashSet<string>(_attributesHeld, StringComparer.OrdinalIgnoreCase);
			known.UnionWith(_attributesSet);
			known.UnionWith(_standardAttributes.Select(entry => entry.Name));
			known.UnionWith(AttributeService.StandardAttributeAliases.Keys);
			Report(result, _attributesRead.Where(pair => !known.Contains(pair.Key) && !AliasHeld(pair.Key, known)),
				(name, tally) => $"Softcode: the attribute {name} is called {Times(tally.Count)}, and no object in the world has it and no code sets it. First in {tally.Where}.",
				rest => $"Softcode: {rest.Count} more attributes are called that no object has and no code sets: {Listed(rest)}.");
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
			return (kind switch
			{
				'f' => ImportLeftOut.LeavesOutFunction(dialect, name),
				's' => ImportLeftOut.LeavesOutSwitch(dialect, name[..name.IndexOf('/')], name[(name.IndexOf('/') + 1)..]),
				_ => ImportLeftOut.LeavesOutCommand(dialect, name)
			})
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

		/// <summary>The names an overflow line lists, at most <see cref="MaxReported"/> of them, and how many more.</summary>
		private static string Listed(IReadOnlyCollection<string> names)
		{
			var listed = string.Join(", ", names.Take(MaxReported));
			return names.Count > MaxReported ? $"{listed} and {names.Count - MaxReported} more" : listed;
		}

		private static string Times(int count) => count == 1 ? "once" : $"{count} times";
	}
}
