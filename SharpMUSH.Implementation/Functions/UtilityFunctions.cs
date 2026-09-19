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
using SharpMUSH.Library.ExpandedObjectData;
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

	[SharpFunction(Name = "die", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> Die(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!int.TryParse((args["0"].Message ?? MarkupText.Empty).ToPlainText(), out var count) || count < 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}
		if (!int.TryParse((args["1"].Message ?? MarkupText.Empty).ToPlainText(), out var sides) || sides <= 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		// Optional third argument for how many rolls to show (vs just return sum)
		var showCount = count;
		if (args.Count == 3)
		{
			if (!int.TryParse((args["2"].Message ?? MarkupText.Empty).ToPlainText(), out showCount) || showCount < 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
			}
		}

		var rolls = new List<int>();
		var total = 0;

		for (int i = 0; i < count; i++)
		{
			var roll = Random.Shared.Next(1, sides + 1);
			rolls.Add(roll);
			total += roll;
		}

		if (showCount < count)
		{
			return ValueTask.FromResult(new CallState(total.ToString()));
		}

		return ValueTask.FromResult(new CallState(string.Join(" ", rolls)));
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


	[SharpFunction(Name = "isdbref", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> IsDbRef(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (HelperFunctions.ParseDbRef((parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText()) is not DBRef dbref) return new("0");
		return new CallState(await Mediator.Send(new GetObjectNodeQuery(dbref)) is AnySharpObject);
	}

	/// <summary>
	/// 64-bit, as <c>fun_isint</c> is (<c>parse_ival_full</c> = <c>parse_int64</c>,
	/// <c>src/funmath.c:82-84</c>). A 32-bit parse said no to every value above 2147483647 that
	/// <c>add()</c>, <c>sub()</c> and <c>div()</c> all handled as an integer.
	/// </summary>
	[SharpFunction(Name = "isint", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsInt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(long.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsNum(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(decimal.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isobjid", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsObjId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = (parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText();
		// Object ID format is #dbref:timestamp (e.g., #123:456789)
		var match = ObjIdRegex().Match(arg);
		return ValueTask.FromResult(new CallState(match.Success ? "1" : "0"));
	}

	[SharpFunction(Name = "isregexp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> isregexp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(arg)) return ValueTask.FromResult<CallState>(new("0"));

		// Validate regex by attempting to construct it with timeout to prevent ReDoS
		// Use a helper method to avoid exception-based control flow
		var isValid = IsValidRegexPattern(arg);
		return ValueTask.FromResult<CallState>(new(isValid ? "1" : "0"));
	}

	private bool IsValidRegexPattern(string pattern)
	{
		try
		{
			// Use a timeout to prevent catastrophic backtracking (ReDoS)
			// This is a validation step, not control flow - we're checking validity
			_ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}

	[SharpFunction(Name = "isword", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = (parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText();
		return ValueTask.FromResult(new CallState(IsWordRegex().IsMatch(str)));
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

	[SharpFunction(Name = "rand", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> Rand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		// Check if first argument exists and is not empty
		if (!args.TryGetValue("0", out var arg0) || string.IsNullOrWhiteSpace((arg0.Message ?? MarkupText.Empty).ToPlainText()))
		{
			// No arguments: random number between 0 and 2^31-1
			return ValueTask.FromResult(new CallState(Random.Shared.Next(0, int.MaxValue)));
		}

		// Check if second argument exists and is not empty
		if (!args.TryGetValue("1", out var arg1) || string.IsNullOrWhiteSpace((arg1.Message ?? MarkupText.Empty).ToPlainText()))
		{
			// One argument: random number from 0 to arg-1
			if (!int.TryParse((arg0.Message ?? MarkupText.Empty).ToPlainText(), out var maxVal))
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
			}
			// PennMUSH behavior: rand(0) is an error (empty range), negative values return 0
			if (maxVal == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ResultOutOfRange));
			}
			if (maxVal < 0)
			{
				return ValueTask.FromResult(new CallState(0));
			}
			return ValueTask.FromResult(new CallState(Random.Shared.Next(0, maxVal)));
		}

		// Two arguments: random number between min and max (inclusive)
		if (!int.TryParse((arg0.Message ?? MarkupText.Empty).ToPlainText(), out var minVal) ||
				!int.TryParse((arg1.Message ?? MarkupText.Empty).ToPlainText(), out var maxVal2))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}
		if (minVal > maxVal2)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}
		// Next is exclusive of upper bound, so add 1
		return ValueTask.FromResult(new CallState(Random.Shared.Next(minVal, maxVal2 + 1)));
	}

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

	[SharpFunction(Name = "soundex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var val)
			? val.Message!.ToPlainText().ToLowerInvariant()
			: "soundex";

		return arg1 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(ComputeSoundex(
				arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0)),
			"phone" => ValueTask.FromResult<CallState>(ComputePhoneticHash(arg0)),
			_ => ValueTask.FromResult<CallState>("#-1 INVALID HASH TYPE")
		};
	}

	[SharpFunction(Name = "soundslike", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments.TryGetValue("2", out var val)
			? val.Message!.ToPlainText().ToLowerInvariant()
			: "soundex";

		return arg2 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(
				ComputeSoundex(arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0) ==
				ComputeSoundex(arg1.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg1[2..] : arg1)
					? "1" : "0"),
			"phone" => ValueTask.FromResult<CallState>(
				ComputePhoneticHash(arg0) == ComputePhoneticHash(arg1) ? "1" : "0"),
			_ => ValueTask.FromResult<CallState>("#-1 INVALID HASH TYPE")
		};
	}

	/// <summary>
	/// Compute the American Soundex code for a string.
	/// Standard mapping: B/F/P/V=1, C/G/J/K/Q/S/X/Z=2, D/T=3, L=4, M/N=5, R=6
	/// </summary>
	private string ComputeSoundex(string input)
	{
		if (string.IsNullOrEmpty(input)) return "0000";

		// Standard American Soundex mapping
		const string soundexMap = "01230120022455012623010202";
		// Index:                   A B C D E F G H I J K L M N O P Q R S T U V W X Y Z

		var result = new char[4];
		result[0] = char.ToUpper(input[0]);
		var lastCode = result[0] >= 'A' && result[0] <= 'Z'
			? soundexMap[result[0] - 'A']
			: '0';
		var count = 1;

		for (var i = 1; i < input.Length && count < 4; i++)
		{
			var c = char.ToUpper(input[i]);
			if (c < 'A' || c > 'Z') continue;

			var code = soundexMap[c - 'A'];
			if (code != '0' && code != lastCode)
			{
				result[count++] = code;
			}
			lastCode = code;
		}

		// Pad with zeros
		while (count < 4)
		{
			result[count++] = '0';
		}

		return new string(result);
	}

	/// <summary>
	/// Compute the phonetic hash using SQLite's spellfix1 algorithm (used by PennMUSH).
	/// Maps characters to phonetic classes, omits vowels beside R/L, deduplicates.
	/// </summary>
	private string ComputePhoneticHash(string input)
	{
		if (string.IsNullOrEmpty(input)) return "";

		var word = input.ToLowerInvariant();
		var result = new System.Text.StringBuilder();
		var length = word.Length;

		// Character classes
		const int SILENT = 0, VOWEL = 1, B = 2, C = 3, D = 4, L = 6, R = 7, M = 8, Y = 9, DIGIT = 10, SPACE = 11, OTHER = 12;

		// className maps class index to output character
		const string classOutput = ".ABCDHLRMY9 ?";

		// midClass lookup (H, W, Y differ in initClass)
		int MidClass(char ch) => ch switch
		{
			>= 'a' and <= 'z' => ch switch
			{
				'a' or 'e' or 'i' or 'o' or 'u' or 'y' => VOWEL,
				'b' or 'f' or 'p' or 'v' or 'w' => B,
				'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => C,
				'd' or 't' => D,
				'h' => SILENT,
				'l' => L,
				'r' => R,
				'm' or 'n' => M,
				_ => OTHER
			},
			>= '0' and <= '9' => DIGIT,
			' ' or '\t' or '\r' or '\n' => SPACE,
			'\'' => SILENT,
			_ => OTHER
		};

		// initClass: same as midClass except H→SILENT, W→B, Y→Y (not VOWEL)
		int InitClass(char ch) => ch switch
		{
			'y' => Y,
			'h' => SILENT,
			_ => MidClass(ch)
		};

		// Drop initial GN/KN
		int start = 0;
		if (length >= 2 && (word[0] == 'g' || word[0] == 'k') && word[1] == 'n')
		{
			start = 1;
		}

		int cPrev = OTHER; // 0x77 maps to OTHER range
		int cPrevX = OTHER;
		bool isFirst = true;

		for (int i = start; i < length; i++)
		{
			var ch = word[i];

			// Skip D before J/G, W before R, T before CH
			if (i + 1 < length)
			{
				if (ch == 'w' && word[i + 1] == 'r') continue;
				if (ch == 'd' && (word[i + 1] == 'j' || word[i + 1] == 'g')) continue;
				if (i + 2 < length && ch == 't' && word[i + 1] == 'c' && word[i + 2] == 'h') continue;
			}

			int c = isFirst ? InitClass(ch) : MidClass(ch);

			if (c == SPACE) continue;
			if (c == OTHER && cPrev != DIGIT) continue;

			isFirst = false;

			// Omit vowels beside R and L
			if (c == VOWEL && (cPrevX == R || cPrevX == L))
			{
				continue;
			}
			if ((c == R || c == L) && cPrevX == VOWEL)
			{
				// Remove the preceding vowel
				if (result.Length > 0) result.Length--;
			}

			cPrev = c;
			if (c == SILENT) continue;
			cPrevX = c;

			char output = classOutput[c];
			// Deduplicate consecutive same output chars
			if (result.Length == 0 || output != result[result.Length - 1])
			{
				result.Append(output);
			}
		}

		return result.ToString();
	}

	[SharpFunction(Name = "suggest", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> Suggest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var category = args["0"].Message!.ToPlainText();
		var word = args["1"].Message!.ToPlainText();
		var separator = args.ContainsKey("2") ? args["2"].Message!.ToPlainText() : " ";
		var limit = 20;

		if (args.ContainsKey("3"))
		{
			if (!int.TryParse(args["3"].Message!.ToPlainText(), out limit) || limit < 1)
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}
		}

		// Get suggestion data from expanded server data
		var suggestionData = await ObjectDataService.GetExpandedServerDataAsync<SuggestionData>();
		if (suggestionData?.Categories == null || !suggestionData.Categories.ContainsKey(category))
		{
			// If category doesn't exist, return empty string
			return new CallState(string.Empty);
		}

		var vocabulary = suggestionData.Categories[category];
		if (vocabulary == null || vocabulary.Count == 0)
		{
			return new CallState(string.Empty);
		}

		// Calculate Levenshtein distance for each word and sort by distance
		var wordLower = word.ToLower();
		var suggestions = vocabulary
			.Select(v => (Word: v, Distance: CalculateLevenshteinDistance(wordLower, v.ToLower())))
			.OrderBy(x => x.Distance)
			.ThenBy(x => x.Word) // Secondary sort by word for consistency
			.Take(limit)
			.Select(x => x.Word);

		return new CallState(string.Join(separator, suggestions));
	}

	/// <summary>
	/// Calculates the Levenshtein distance between two strings.
	/// This is the minimum number of single-character edits (insertions, deletions, or substitutions)
	/// required to change one word into the other.
	/// </summary>
	private static int CalculateLevenshteinDistance(string source, string target)
	{
		if (string.IsNullOrEmpty(source))
		{
			return string.IsNullOrEmpty(target) ? 0 : target.Length;
		}

		if (string.IsNullOrEmpty(target))
		{
			return source.Length;
		}

		// Each row of the distance table depends only on the row before it, so two rows suffice.
		var width = target.Length + 1;
		Span<int> previous = width <= 128 ? stackalloc int[width] : new int[width];
		Span<int> current = width <= 128 ? stackalloc int[width] : new int[width];

		for (var j = 0; j < width; j++)
		{
			previous[j] = j;
		}

		for (var i = 1; i <= source.Length; i++)
		{
			current[0] = i;
			for (var j = 1; j <= target.Length; j++)
			{
				var cost = source[i - 1] == target[j - 1] ? 0 : 1;

				current[j] = Math.Min(
					Math.Min(
						previous[j] + 1,      // Deletion
						current[j - 1] + 1),  // Insertion
					previous[j - 1] + cost);  // Substitution
			}

			var finished = current;
			current = previous;
			previous = finished;
		}

		return previous[target.Length];
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

	[GeneratedRegex(@"^#\d+:\d+$")]
	private static partial Regex ObjIdRegex();

	[GeneratedRegex(@"^[a-zA-Z]+$")]
	private static partial Regex IsWordRegex();
}
