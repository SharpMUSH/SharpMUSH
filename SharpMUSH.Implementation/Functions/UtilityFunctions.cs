using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Markup;
using SharpMUSH.Implementation.Definitions;
using DotNext;
using DotNext.Collections.Generic;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Drawing;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "ansi", MinArgs = 2, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> ANSI(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		// TODO: Move ANSI color processing to AnsiMarkup module for better integration.
		// This would allow align() and other markup functions to work directly with parsed ANSI structures.
		AnsiColor? foreground = null;
		AnsiColor? background = null;
		var blink = false;
		var bold = false;
		var clear = false;
		var invert = false;
		var underline = false;

		// NOT a plain Split(' '): PennMUSH's angle-bracket RGB form is "a list of red, green and
		// blue values from 0-255, in angle brackets" (help ANSI()), so <255 0 0> contains the very
		// separator the code list is split on. Splitting naively tore it into "<255", "0", "0>" -
		// the <...> branch below could never fire, and the stray "0" was then read as xterm 0, so
		// ansi(<255 0 0>,test) silently produced some other colour entirely.
		var ansiCodes = AnsiCodeTokenRegex().Matches(args["0"].Message!.ToPlainText());
		var colorsConfig = ColorConfiguration?.CurrentValue;

		foreach (Match token in ansiCodes)
		{
			var code = token.ValueSpan;
			var curHilight = false;
			var isBackground = false;

			if (code.StartsWith("/"))
			{
				isBackground = true;
				code = code[1..];
			}

			if (code.StartsWith("#"))
			{
				// Handle RGB color (hex code)
				var color = ColorTranslator.FromHtml(code.ToString()).ToAnsiColor();
				if (isBackground)
					background = color;
				else
					foreground = color;
				continue;
			}

			if (code.StartsWith(['+']) && !code.StartsWith("+xterm"))
			{
				// Handle named color from colors.json
				var colorName = code[1..].ToString();
				if (colorsConfig != null && colorsConfig.ColorsByName.TryGetValue(colorName, out var colorIdentity))
				{
					var hexColor = colorIdentity.rgb;
					var color = ColorTranslator.FromHtml(hexColor).ToAnsiColor();
					if (isBackground)
						background = color;
					else
						foreground = color;
				}
				continue;
			}

			var xterm = 0;
			if (
				(int.TryParse(code, out xterm) && xterm >= 0 && xterm < 256) ||
				(code.StartsWith("+xterm") && int.TryParse(code[6..], out xterm) && xterm >= 0 && xterm < 256))
			{
				// Handle xterm color (0-255)
				if (colorsConfig != null && colorsConfig.ColorsByXterm.TryGetValue(xterm.ToString(), out var xtermColors) && xtermColors.Length > 0)
				{
					var hexColor = xtermColors[0].rgb;
					var color = ColorTranslator.FromHtml(hexColor).ToAnsiColor();
					if (isBackground)
						background = color;
					else
						foreground = color;
				}
				continue;
			}

			if (code.StartsWith(['<']) && code.EndsWith(['>']))
			{
				// Handle RGB color as <r g b> format
				var rgbValues = code[1..^1].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (rgbValues.Length == 3 &&
					int.TryParse(rgbValues[0], out var r) && r >= 0 && r <= 255 &&
					int.TryParse(rgbValues[1], out var g) && g >= 0 && g <= 255 &&
					int.TryParse(rgbValues[2], out var b) && b >= 0 && b <= 255)
				{
					var color = Color.FromArgb(r, g, b).ToAnsiColor();
					if (isBackground)
						background = color;
					else
						foreground = color;
				}
				continue;
			}

			// Reset isBackground for character-by-character processing
			isBackground = false;
			foreach (var chr in code)
			{
				switch (chr)
				{
					case 'i':
						invert = true;
						break;
					case 'I':
						invert = false;
						break;
					case 'f':
						blink = true;
						break;
					case 'F':
						blink = false;
						break;
					case 'u':
						underline = true;
						break;
					case 'U':
						underline = false;
						break;
					case 'h':
						// A per-token modifier that raises the FOLLOWING foreground letter to its bright
						// variant (hr is AnsiColor.Standard(1, bright: true)); a background has no bright
						// variant, so there it means bold instead. On its own it carries nothing, which
						// matches AnsiCodeParser — the two must agree, or ansi() and the parsed form of
						// the same code produce different markup.
						curHilight = true;
						break;
					case 'H':
						curHilight = false;
						break;
					case 'n':
						// ANSI 'n' (clear/normal) resets all formatting to defaults.
						// Setting clear=true adds a clear ANSI code to the output,
						// while resetting the fields ensures the structure has no formatting.
						clear = true;
						foreground = null;
						background = null;
						blink = false;
						bold = false;
						invert = false;
						underline = false;
						curHilight = false;
						break;
					case 'd':
						foreground = AnsiColor.Default.Instance;
						break;
					case 'x':
						foreground = new AnsiColor.Standard(0, curHilight);
						break;
					case 'r':
						foreground = new AnsiColor.Standard(1, curHilight);
						break;
					case 'g':
						foreground = new AnsiColor.Standard(2, curHilight);
						break;
					case 'y':
						foreground = new AnsiColor.Standard(3, curHilight);
						break;
					case 'b':
						foreground = new AnsiColor.Standard(4, curHilight);
						break;
					case 'm':
						foreground = new AnsiColor.Standard(5, curHilight);
						break;
					case 'c':
						foreground = new AnsiColor.Standard(6, curHilight);
						break;
					case 'w':
						foreground = new AnsiColor.Standard(7, curHilight);
						break;
					case 'D':
						background = AnsiColor.Default.Instance;
						break;
					case 'X':
						background = new AnsiColor.Standard(0, false);
						bold |= curHilight;
						break;
					case 'R':
						background = new AnsiColor.Standard(1, false);
						bold |= curHilight;
						break;
					case 'G':
						background = new AnsiColor.Standard(2, false);
						bold |= curHilight;
						break;
					case 'Y':
						background = new AnsiColor.Standard(3, false);
						bold |= curHilight;
						break;
					case 'B':
						background = new AnsiColor.Standard(4, false);
						bold |= curHilight;
						break;
					case 'M':
						background = new AnsiColor.Standard(5, false);
						bold |= curHilight;
						break;
					case 'C':
						background = new AnsiColor.Standard(6, false);
						bold |= curHilight;
						break;
					case 'W':
						background = new AnsiColor.Standard(7, false);
						bold |= curHilight;
						break;
					default:
						// Do nothing. Just skip.
						// Should probably warn about invalid ansi codes.
						break;
				}
			}
		}

		var details = new AnsiStyle
		{
			Foreground = foreground,
			Background = background,
			Blink = blink,
			Bold = bold,
			Clear = clear,
			Inverted = invert,
			Underlined = underline,
			Faint = false,
			Italic = false,
			Overlined = false,
			StrikeThrough = false,
			LinkText = null,
			LinkUrl = null
		};

		return ValueTask.FromResult(new CallState(MarkupText.Wrap(new Ansi(details), args["1"].Message ?? MarkupText.Empty)));
	}

	[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public ValueTask<CallState> AtAt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(string.Empty));

	[SharpFunction(Name = "allof", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["value..."])]
	public async ValueTask<CallState> AllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		// PennMUSH allof(val1, val2, ..., valN, delimiter):
		// The last argument is the output delimiter.
		// Evaluate each of the remaining arguments.
		// Return all truthy values joined by the delimiter.
		if (args.Count < 2)
		{
			// With 0 or 1 arg, there's no delimiter — just return empty
			return CallState.Empty;
		}

		var delimArg = args[(args.Count - 1).ToString()];
		var delimParsed = await parser.FunctionParse(delimArg.Message!);
		var delimiter = delimParsed?.Message ?? MarkupText.Empty;
		var hadErrors = delimParsed?.HadErrors == true;

		var truthyValues = new List<MString>();
		for (var i = 0; i < args.Count - 1; i++)
		{
			var parsed = await parser.FunctionParse(args[i.ToString()].Message!);
			hadErrors |= parsed?.HadErrors == true;
			var value = parsed?.Message ?? MarkupText.Empty;
			if (value.Truthy(parser)) truthyValues.Add(value);
		}

		return new CallState(MarkupText.Join(delimiter, truthyValues)) { HadErrors = hadErrors };
	}

	[SharpFunction(Name = "beep", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.AdminOnly | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> Beep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var count = 1;
		if (!parser.CurrentState.Arguments.TryGetValue("0", out var arg))
		{
			return ValueTask.FromResult(new CallState(new string('\a', count)));
		}

		var str = arg.Message!.ToPlainText();
		if (int.TryParse(str, out var parsed) && parsed is >= 1 and <= 5)
			count = parsed;

		return ValueTask.FromResult(new CallState(new string('\a', count)));
	}

	[SharpFunction(Name = "benchmark", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
	public async ValueTask<CallState> Benchmark(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		var code = args["0"].Message!;

		if (!int.TryParse((args["1"].Message ?? MarkupText.Empty).ToPlainText(), out var iterations) || iterations <= 0)
		{
			return new CallState(ErrorMessages.Returns.Numbers);
		}

		var outputFormat = "ms";
		if (args.Count >= 3 && args.TryGetValue("2", out var formatArg))
		{
			outputFormat = (formatArg.Message ?? MarkupText.Empty).ToPlainText().ToLower();
		}

		var hadErrors = false;
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		for (int i = 0; i < iterations; i++)
		{
			hadErrors |= (await parser.FunctionParse(code))?.HadErrors == true;
		}
		stopwatch.Stop();

		var elapsed = stopwatch.Elapsed.TotalMilliseconds;
		if (outputFormat == "s" || outputFormat == "seconds")
		{
			return new CallState((elapsed / 1000.0).ToString("F6")) { HadErrors = hadErrors };
		}
		else
		{
			return new CallState(elapsed.ToString("F3")) { HadErrors = hadErrors };
		}
	}

	/// <remarks>
	/// PennMUSH's <c>fun_checkpass</c> resolves its first argument with <c>lookup_player</c>, so a name
	/// works as well as a dbref; resolving it with a dbref parse alone answered
	/// <c>#-1 NO SUCH PLAYER</c> for every call that named a player.
	/// </remarks>
	[SharpFunction(Name = "checkpass", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "password"])]
	public async ValueTask<CallState> Checkpass(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = (parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText();

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, target,
			player => ValueTask.FromResult<CallState>(
				PasswordService.PasswordIsValid(
					$"#{player.Object.Key}:{player.Object.CreationTime}",
					parser.CurrentState.Arguments["1"].Message!.ToPlainText(),
					player.PasswordHash)
					? "1"
					: "0"));
	}

	[SharpFunction(Name = "fn", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public async ValueTask<CallState> Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var functionName = (parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText();
		if (string.IsNullOrWhiteSpace(functionName))
		{
			return new CallState("#-1 FUNCTION (No function name given)");
		}

		if (parser.FunctionLibrary.TryGetValue(functionName.ToLower(), out var targetFunction))
		{
			EvaluationRestrictions.Demand(targetFunction.LibraryInformation, parser.CurrentState.Restrictions);
			using var retainedSource = RestrictedTextRetention.Enter(parser.CurrentState);
			if (retainedSource is not null)
			{
				// Reserve before ToPlainText/Join allocate; the recursive parse keeps
				// this source live until it returns, including chains of fn targets.
				retainedSource.Add(functionName.Length + 2L);
				var first = true;
				foreach (var argument in parser.CurrentState.ArgumentsOrdered.Skip(1))
				{
					retainedSource.Add((argument.Value.Message?.Length ?? 0) + (first ? 0L : 1L));
					first = false;
				}
			}
			// Build function call string and re-parse: fn(add,1,2) -> add(1,2)
			var fnArgs = parser.CurrentState.ArgumentsOrdered
				.Skip(1)
				.Select(x => (x.Value.Message ?? MarkupText.Empty).ToPlainText());
			var callString = $"{functionName}({string.Join(",", fnArgs)})";
			var result = await parser.FunctionParse(MarkupText.Plain(callString));
			return result ?? CallState.Empty;
		}

		// Fall back to user-defined attribute function only when object-data access is allowed.
		EvaluationRestrictions.DemandObjectDataAccess(parser.CurrentState.Restrictions);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var result2 = await AttributeService.EvaluateAttributeFunctionResultAsync(
			parser,
			executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);

		// If attribute lookup returned nothing, report function not found
		if (result2.Message is null || result2.Message.ToPlainText().Length == 0)
		{
			return new CallState($"#-1 FUNCTION ({functionName.ToUpper()}) NOT FOUND") { HadErrors = result2.HadErrors };
		}

		return result2;
	}
	[SharpFunction(Name = "functions", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> FFunctions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var functionLibrary = parser.FunctionLibrary;

		var pattern = "*";
		if (parser.CurrentState.Arguments.TryGetValue("0", out var arg0))
		{
			var patternArg = (arg0.Message ?? MarkupText.Empty).ToPlainText();
			if (!string.IsNullOrWhiteSpace(patternArg))
			{
				pattern = patternArg;
			}
		}

		var allFunctions = functionLibrary.Keys.OrderBy(x => x);

		IEnumerable<string> filteredFunctions;
		if (pattern == "*")
		{
			filteredFunctions = allFunctions;
		}
		else
		{
			var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
			var regex = SoftcodeRegex.Create(regexPattern, RegexOptions.IgnoreCase);
			filteredFunctions = allFunctions.Where(name => SoftcodeRegex.IsMatch(regex, name));
		}

		return ValueTask.FromResult(new CallState(string.Join(" ", filteredFunctions)));
	}

	[SharpFunction(Name = "list", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> List(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		if (args.Count == 0)
		{
			return CallState.Empty;
		}

		var option = args.TryGetValue("0", out var a0) ? (a0.Message ?? MarkupText.Empty).ToPlainText().Trim().ToLowerInvariant() : string.Empty;
		var type = args.TryGetValue("1", out var a1) ? (a1.Message ?? MarkupText.Empty).ToPlainText().Trim().ToLowerInvariant() : string.Empty;

		static string JoinSpace(IEnumerable<string> items) => string.Join(' ', items.Where(s => !string.IsNullOrWhiteSpace(s)));

		switch (option)
		{
			case "motd":
				{
					return await Motd(parser, default!);
				}
			case "wizmotd":
			case "downmotd":
			case "fullmotd":
				{
					return await GetWizardMotdAsync(parser, option);
				}
			case "functions":
				{
					var funcPairs = type switch
					{
						"builtin" => parser.FunctionLibrary.AsEnumerable().Where(kv => kv.Value.IsSystem),
						"local" => parser.FunctionLibrary.AsEnumerable().Where(kv => !kv.Value.IsSystem),
						_ => parser.FunctionLibrary.AsEnumerable()
					};

					var names = funcPairs
						.Select(kv => kv.Value.LibraryInformation.Attribute.Name)
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
						.Select(s => s.ToLowerInvariant());
					return new CallState(JoinSpace(names));
				}
			case "commands":
				{
					var cmdPairs = type switch
					{
						"builtin" => parser.CommandLibrary.AsEnumerable().Where(kv => kv.Value.IsSystem),
						"local" => parser.CommandLibrary.AsEnumerable().Where(kv => !kv.Value.IsSystem),
						_ => parser.CommandLibrary.AsEnumerable()
					};

					var names = cmdPairs
						.Select(kv => kv.Value.LibraryInformation.Attribute.Name)
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
						.Select(s => s.ToLowerInvariant());
					return new CallState(JoinSpace(names));
				}
			case "attribs":
				return await SortedNames(Mediator.CreateStream(new GetAllAttributeEntriesQuery()).Select(x => x.Name));
			case "locks":
				{
					var lockNames = Enum.GetNames(typeof(LockType))
						.Select(n => n.ToLowerInvariant())
						.OrderBy(x => x);
					return new CallState(JoinSpace(lockNames));
				}
			case "flags":
				return await SortedNames(Mediator.CreateStream(new GetAllObjectFlagsQuery()).Select(x => x.Name));
			case "powers":
				return await SortedNames(Mediator.CreateStream(new GetPowersQuery()).Select(x => x.Name));
			default:
				return CallState.Empty;
		}

		static async ValueTask<CallState> SortedNames(IAsyncEnumerable<string> names)
		{
			var list = await names.Select(name => name.ToLowerInvariant()).ToListAsync();
			list.Sort(StringComparer.OrdinalIgnoreCase);
			return new CallState(JoinSpace(list));
		}

		async ValueTask<CallState> GetWizardMotdAsync(IMUSHCodeParser parser, string option)
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			if (!await executor.IsWizard())
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			return option switch
			{
				"wizmotd" => await WizMotd(parser, default!),
				"downmotd" => await DownMotd(parser, default!),
				"fullmotd" => await FullMotd(parser, default!),
				_ => CallState.Empty
			};
		}
	}

	[SharpFunction(Name = "listset", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> ListSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!args.TryGetValue("0", out var arg0))
		{
			return ValueTask.FromResult(CallState.Empty);
		}
		var listStr = arg0.Message!;

		if (!args.TryGetValue("1", out var arg1) ||
				!int.TryParse((arg1.Message ?? MarkupText.Empty).ToPlainText(), out var position))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		if (!args.TryGetValue("2", out var arg2))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}
		var newValue = arg2.Message!;

		var inputDelimiter = " ";
		if (args.TryGetValue("3", out var arg3))
		{
			inputDelimiter = (arg3.Message ?? MarkupText.Empty).ToPlainText();
		}

		var outputDelimiter = inputDelimiter;
		if (args.TryGetValue("4", out var arg4))
		{
			outputDelimiter = (arg4.Message ?? MarkupText.Empty).ToPlainText();
		}

		var items = MushText.SplitList(MarkupText.Plain(inputDelimiter), listStr);

		if (position < 1 || position > items.Length)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));
		}

		// Set the item at the position (convert to 0-based)
		items[position - 1] = newValue;

		return ValueTask.FromResult(new CallState(MarkupText.Join(MarkupText.Plain(outputDelimiter), items)));
	}

	[SharpFunction(Name = "null", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> Null(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CallState.Empty);

	/// <summary>
	/// <c>render(&lt;string&gt;, &lt;formats&gt;)</c> — PennMUSH's <c>fun_render</c>: turn a string's
	/// markup into wire format for something outside the game, a bot or a web page.
	///
	/// <para>What stood here was an objeval: it located an object, checked Controls and evaluated the
	/// second argument as that object — which is what <c>objeval()</c> already does, under a name the
	/// helpfile gave to something else entirely.</para>
	/// </summary>
	/// <remarks>
	/// PennMUSH's <c>markup</c> flag asks for whatever the other flags did not handle to survive as
	/// internal markup tags. SharpMUSH holds markup as layers over the text rather than as inline
	/// tags, and renders a whole string to one format, so there is nothing for the flag to leave
	/// behind: it is accepted and changes nothing. See <c>pennmush-compatibility.md</c>.
	/// </remarks>
	[SharpFunction(Name = "render", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "formats"])]
	public async ValueTask<CallState> Render(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var text = args["0"].Message ?? MarkupText.Empty;

		var ansi = false;
		var html = false;
		var noAccents = false;

		foreach (var format in args["1"].Message!.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			// PennMUSH prefix-matches "noaccents" alone; the other three are spelled in full.
			if (format.Equals("ansi", StringComparison.OrdinalIgnoreCase)) ansi = true;
			else if (format.Equals("html", StringComparison.OrdinalIgnoreCase)) html = true;
			else if ("noaccents".StartsWith(format, StringComparison.OrdinalIgnoreCase)) noAccents = true;
			else if (format.Equals("markup", StringComparison.OrdinalIgnoreCase)) { }
			else return new CallState(ErrorMessages.Returns.InvalidSecondArgument);
		}

		// Raw colour codes are a spoofing tool wherever the result is echoed back into the game.
		if (ansi && !await PermissionService.CanNoSpoof(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var rendered = (html, ansi) switch
		{
			(true, _) => text.Render(MarkupFormat.Html),
			(_, true) => text.Render(MarkupFormat.Ansi),
			_ => text.ToPlainText()
		};

		return new CallState(noAccents ? RemoveDiacritics(rendered) : rendered);
	}

	[SharpFunction(Name = "s", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public async ValueTask<CallState> S(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> (await parser.FunctionParse(parser.CurrentState.Arguments.Last().Value.Message!))!;

	private static async ValueTask<Func<DBRef, CancellationToken, ValueTask<bool>>> ObserveProjectionRealityAsync(
		IMUSHCodeParser parser, DBRef receiver)
	{
		var reality = parser.ServiceProvider.GetRequiredService<IRealityPolicy>();
		return reality is IRealityObservationProvider observations
			? await observations.ObserveAsync(receiver, ExecutionBudget.CurrentToken)
			: (target, token) => reality.CanPerceiveAsync(receiver, target, token);
	}

	[SharpFunction(Name = "scan", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> Scan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// Parse arguments based on PennMUSH signature: scan(<looker>, <command>[, <switches>]) or scan(<command>)
		AnySharpObject looker;
		MString command;
		string switches;

		if (args.Count == 1)
		{
			// scan(<command>) - looker defaults to executor
			looker = executor;
			command = args["0"].Message!;
			switches = "all";
		}
		else
		{
			// scan(<looker>, <command>[, <switches>])
			var lookerName = args["0"].Message!.ToPlainText();
			command = args["1"].Message!;
			switches = args.ContainsKey("2") ? args["2"].Message!.ToPlainText() : "all";

			var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, lookerName, LocateFlags.All);
			if (locateResult is not AnySharpObject located)
			{
				return CallState.Empty;
			}
			looker = located;
		}

		if (!await PermissionService.Controls(executor, looker))
		{
			return ErrorMessages.Returns.PermissionDenied;
		}

		var objectsToScan = new List<AnySharpObject>();
		var switchList = switches.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

		bool checkMe = switchList.Contains("me") || switchList.Contains("all") || switchList.Contains("self");
		bool checkInventory = switchList.Contains("inventory") || switchList.Contains("all") || switchList.Contains("self");
		bool checkRoom = switchList.Contains("room") || switchList.Contains("all");
		bool checkGlobals = switchList.Contains("globals") || switchList.Contains("all");

		if (checkMe)
		{
			objectsToScan.Add(looker);
		}

		async ValueTask AddContents(AnySharpContainer container)
		{
			await foreach (var item in Mediator.CreateStream(new GetContentsQuery(container)))
			{
				objectsToScan.Add(item.WithRoomOption());
			}
		}

		if (checkInventory && looker.IsContainer)
		{
			await AddContents(looker.AsContainer);
		}

		if (checkRoom)
		{
			var locationOpt = await Mediator.Send(new GetLocationQuery(looker.Object().DBRef));

			if (locationOpt is AnySharpContainer location)
			{
				objectsToScan.Add(location.WithExitOption());
				await AddContents(location);
			}
		}

		if (checkGlobals)
		{
			if (await Mediator.Send(new GetObjectNodeQuery(new DBRef(0))) is AnySharpObject masterRoom)
			{
				objectsToScan.Add(masterRoom);

				if (masterRoom.IsContainer)
				{
					await AddContents(masterRoom.AsContainer);
				}
			}
		}

		var perceive = await ObserveProjectionRealityAsync(parser, executor.Object().DBRef);
		var uniqueObjects = objectsToScan
			.Distinct()
			.ToAsyncEnumerable()
			.Where(async (obj, _) => await perceive(obj.Object().DBRef, ExecutionBudget.CurrentToken));

		var matchResult = await CommandDiscoveryService.MatchUserDefinedCommand(
			parser,
			uniqueObjects,
			command);

		if (!matchResult.TryGetValue(out var matches))
		{
			return CallState.Empty;
		}

		// Format results as "dbref/attribute" pairs
		var results = matches.Select(match =>
			$"{match.SObject.Object().DBRef}/{match.Attribute.Name}");

		return string.Join(" ", results);
	}

	[SharpFunction(Name = "textentries", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var separator = args.TryGetValue("1", out var sep)
			? sep.Message!.ToPlainText()
			: " ";

		if (TextFileService == null)
		{
			return new CallState(ErrorMessages.Returns.TextFileServiceNotAvailable);
		}

		try
		{
			var entries = await TextFileService.ListEntriesAsync(fileReference, separator);
			return new CallState(entries);
		}
		catch (FileNotFoundException)
		{
			return new CallState(ErrorMessages.Returns.FileNotFound);
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error in textentries({File})", fileReference);
			return new CallState(ErrorMessages.Returns.Error);
		}
	}

	[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var entryName = args["1"].Message!.ToPlainText();

		if (TextFileService == null)
		{
			return new CallState(ErrorMessages.Returns.TextFileServiceNotAvailable);
		}

		try
		{
			var content = await TextFileService.GetEntryAsync(fileReference, entryName);
			return content != null
				? new CallState(content)
				: new CallState(ErrorMessages.Returns.EntryNotFound);
		}
		catch (FileNotFoundException)
		{
			return new CallState(ErrorMessages.Returns.FileNotFound);
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error in textfile({File}, {Entry})", fileReference, entryName);
			return new CallState(ErrorMessages.Returns.Error);
		}
	}

	/// <summary>
	/// One <c>ansi()</c> code token: either a run ending in an angle-bracketed group (so
	/// <c>&lt;255 0 0&gt;</c> and <c>/&lt;255 0 0&gt;</c> survive intact, spaces and all), or an
	/// ordinary run of non-space characters.
	/// </summary>
	[GeneratedRegex(@"[^\s<]*<[^>]*>|\S+")]
	private static partial Regex AnsiCodeTokenRegex();
}
