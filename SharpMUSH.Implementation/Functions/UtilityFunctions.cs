using System.Drawing;
using System.Text.RegularExpressions;
using DotNext.Collections.Generic;
using MarkupString;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using XSoundex;
using static ANSILibrary.ANSI;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// TODO: Not compatible due to not being able to indicate a DBREF
	[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static async ValueTask<CallState> PCreate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var location = await Mediator!.Send(new GetObjectNodeQuery(new DBRef
		{
			Number = Convert.ToInt32(Configuration!.CurrentValue.Database.PlayerStart)
		}));

		var trueLocation = location.Match(
			player => player.Object.Key,
			room => room.Object.Key,
			exit => exit.Object.Key,
			thing => thing.Object.Key,
			none => -1);

		var created = await Mediator.Send(new CreatePlayerCommand(
			args["0"].Message!.ToString(),
			args["1"].Message!.ToString(),
			new DBRef(trueLocation == -1 ? 1 : trueLocation)));

		return new CallState($"#{created.Number}:{created.CreationMilliseconds}");
	}

	[SharpFunction(Name = "ansi", MinArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ANSI(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		// TODO: Move this to AnsiMarkup, to get a parsed Markup.
		// That way, align() has access to it.
		var foreground = AnsiColor.NoAnsi;
		var background = AnsiColor.NoAnsi;
		var blink = false;
		var bold = false;
		var clear = false;
		var invert = false;
		var underline = false;

		var ansiCodes = args["0"].Message!.ToString().Split(' ');
		Func<bool, byte, byte[]> highlightFunc = (highlight, b) => highlight ? [1, b] : [b];

		foreach (var cde in ansiCodes)
		{
			var code = cde.AsSpan();
			var curHilight = false;
			if (code.StartsWith(['#']) || code.StartsWith("/#"))
			{
				// TODO: Handle background.
				foreground = AnsiColor.NewRGB(ColorTranslator.FromHtml(code[1..].ToString()));
				continue;
			}
			if (code.StartsWith(['+']) && !code.StartsWith("+xterm"))
			{
				// colorname
				continue;
			}
			var xterm = 0;
			if (
				(int.TryParse(code, out xterm) && xterm > -1 && xterm < 256) ||
				(code.StartsWith("+xterm") && int.TryParse(code[5..], out xterm) && xterm > -1 && xterm < 256))
			{
				// xterm color
				continue;
			}
			if (code.StartsWith(['<']) && code.EndsWith(['>']))
			{
				// Check for triple RGB values
				continue;
			}
			// ansi code. each gets evaluated individually.
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
						curHilight = true;
						break;
					case 'H':
						curHilight = false;
						break;
					case 'n':
						clear = true; // TODO: This PROBABLY needs better handling. No doubt this is not correct due to the tree structure.
						break;
					case 'd':
						// TODO: Inline this as a function.
						foreground = StringExtensions.ansiByte(39);
						break;
					case 'x':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 30));
						break;
					case 'r':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 31));
						break;
					case 'g':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 32));
						break;
					case 'y':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 33));
						break;
					case 'b':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 34));
						break;
					case 'm':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 35));
						break;
					case 'c':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 36));
						break;
					case 'w':
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 37));
						break;
					case 'D':
						background = StringExtensions.ansiByte(49);
						break;
					case 'X':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 40));
						break;
					case 'R':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 41));
						break;
					case 'G':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 42));
						break;
					case 'Y':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 43));
						break;
					case 'B':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 44));
						break;
					case 'M':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 45));
						break;
					case 'C':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 46));
						break;
					case 'W':
						background = StringExtensions.ansiBytes(highlightFunc(curHilight, 47));
						break;
					default:
						// Do nothing. Just skip.
						// Should probably warn about invalid ansi codes.
						break;
				}
			}
		}

		var details = new MarkupImplementation.AnsiStructure(
			foreground: foreground,
			background: background,
			blink: blink,
			bold: bold,
			clear: clear,
			inverted: invert,
			underlined: underline,
			faint: false,
			italic: false,
			overlined: false,
			strikeThrough: false,
			linkText: null,
			linkUrl: null);

		return ValueTask.FromResult(new CallState(MModule.markupSingle2(new Ansi(details), args["1"].Message)));
	}

	[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> AtAt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(string.Empty));

	[SharpFunction(Name = "allof", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> AllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var allTrue = true;
		CallState? lastResult = null;
		
		// Evaluate all arguments (no short-circuit like and())
		foreach (var arg in args)
		{
			lastResult = await parser.FunctionParse(arg.Value.Message!);
			var resultStr = MModule.plainText(lastResult!.Message).Trim();
			
			// Check if false (0, empty, #-1, or false)
			if (string.IsNullOrEmpty(resultStr) || 
			    resultStr == "0" || 
			    resultStr.StartsWith("#-1") ||
			    resultStr.Equals("false", StringComparison.OrdinalIgnoreCase))
			{
				allTrue = false;
			}
		}
		
		// Return 1 if all true, 0 otherwise
		return new CallState(allTrue ? "1" : "0");
	}

	[SharpFunction(Name = "atrlock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AtrLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement atrlock - requires lock service integration
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "beep", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.AdminOnly | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Beep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var count = 1;
		if (!parser.CurrentState.Arguments.TryGetValue("0", out var arg))
		{
			return ValueTask.FromResult(new CallState(new string('\a', count)));
		}

		var str = arg.Message!.ToString();
		if (int.TryParse(str, out var parsed) && parsed is >= 1 and <= 5)
			count = parsed;

		return ValueTask.FromResult(new CallState(new string('\a', count)));
	}

	[SharpFunction(Name = "benchmark", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Benchmark(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		
		// First argument is the code to benchmark
		var code = args["0"].Message!;
		
		// Second argument is the number of iterations
		if (!int.TryParse(MModule.plainText(args["1"].Message), out var iterations) || iterations <= 0)
		{
			return new CallState(Errors.ErrorNumbers);
		}
		
		// Optional third argument for output format (defaults to milliseconds)
		var outputFormat = "ms";
		if (args.Count >= 3 && args.TryGetValue("2", out var formatArg))
		{
			outputFormat = MModule.plainText(formatArg.Message).ToLower();
		}
		
		// Benchmark the code
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		for (int i = 0; i < iterations; i++)
		{
			await parser.FunctionParse(code);
		}
		stopwatch.Stop();
		
		// Return the elapsed time in the requested format
		var elapsed = stopwatch.Elapsed.TotalMilliseconds;
		if (outputFormat == "s" || outputFormat == "seconds")
		{
			return new CallState((elapsed / 1000.0).ToString("F6"));
		}
		else
		{
			// Default to milliseconds
			return new CallState(elapsed.ToString("F3"));
		}
	}

	[SharpFunction(Name = "checkpass", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Checkpass(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbRefConversion = HelperFunctions.ParseDbRef(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		if (dbRefConversion.IsNone())
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
			return new CallState("#-1 NO SUCH PLAYER");
		}

		var dbRef = dbRefConversion.AsValue();
		var objectInfo = await Mediator!.Send(new GetObjectNodeQuery(dbRef));
		if (!objectInfo.IsPlayer)
		{
			return new CallState("#-1 NO SUCH PLAYER");
		}

		var player = objectInfo.AsPlayer;

		var result = PasswordService!.PasswordIsValid(
			$"#{player.Object.Key}:{player.Object.CreationTime}",
			parser.CurrentState.Arguments["1"].Message!.ToString(),
			player.PasswordHash);

		return result ? new("1") : new("0");
	}

	[SharpFunction(Name = "clone", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Clone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement clone - requires database object cloning
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "create", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Create(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var name = args["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!await ValidateService!.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			await NotifyService!.Notify(executor, "Invalid name for a thing.");
			return new CallState(Errors.ErrorBadObjectName);
		}
		
		var thing = await Mediator!.Send(new CreateThingCommand(name.ToPlainText(),
			await executor.Where(),
			await executor.Object()
				.Owner.WithCancellation(CancellationToken.None)));
		
		return new CallState(thing.ToString());
		return ValueTask.FromResult(new CallState(
			showCount < count ? total.ToString() : string.Join(" ", rolls)
		));
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}
		if (!int.TryParse(MModule.plainText(args["1"].Message), out var sides) || sides <= 0)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}
		
		// Optional third argument for how many rolls to show (vs just return sum)
		var showCount = count;
		if (args.Count == 3)
		{
			if (!int.TryParse(MModule.plainText(args["2"].Message), out showCount) || showCount < 0)
			{
				return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
			}
		}
		
		var random = new Random();
		var rolls = new List<int>();
		var total = 0;
		
		for (int i = 0; i < count; i++)
		{
			var roll = random.Next(1, sides + 1);
			rolls.Add(roll);
			total += roll;
		}
		
		// If showCount is specified and less than count, only show that many rolls
		if (showCount < count)
		{
			return ValueTask.FromResult(new CallState(total.ToString()));
		}
		else
		{
			// Show individual rolls
			return ValueTask.FromResult(new CallState(string.Join(" ", rolls)));
		}
	}
	[SharpFunction(Name = "dig", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Dig(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement dig - requires room creation
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}
	[SharpFunction(Name = "fn", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// fn() is like ufun() but the first argument is the function name
		// and remaining arguments are passed to the function
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Get the function name from the first argument
		var functionName = parser.CurrentState.Arguments["0"].Message!;
		
		// Call the attribute function with remaining arguments
		var result = await AttributeService!.EvaluateAttributeFunctionAsync(
			parser,
			executor,
			objAndAttribute: functionName,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);

		return new CallState(result);
	}
	[SharpFunction(Name = "functions", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> FFunctions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Get the function library from the parser
		var functionLibrary = parser.FunctionLibrary;
		
		// Get optional pattern argument
		var pattern = "*";
		if (parser.CurrentState.Arguments.TryGetValue("0", out var arg0))
		{
			var patternArg = MModule.plainText(arg0.Message);
			if (!string.IsNullOrWhiteSpace(patternArg))
			{
				pattern = patternArg;
			}
		}
		
		// Get all function names
		var allFunctions = functionLibrary.Keys.OrderBy(x => x);
		
		// Filter by pattern if specified (simple wildcard matching)
		IEnumerable<string> filteredFunctions;
		if (pattern == "*")
		{
			filteredFunctions = allFunctions;
		}
		else
		{
			// Convert simple wildcard pattern to regex
			var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
			var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
			filteredFunctions = allFunctions.Where(name => regex.IsMatch(name));
		}
		
		// Return space-separated list of function names
		return ValueTask.FromResult(new CallState(string.Join(" ", filteredFunctions)));
	}


	[SharpFunction(Name = "isdbref", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> IsDbRef(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var parsed = HelperFunctions.ParseDbRef(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		if (parsed.IsNone()) return new("0");
		return new CallState(!(await Mediator!.Send(new GetObjectNodeQuery(parsed.AsValue()))).IsNone);
	}

	[SharpFunction(Name = "isint", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsInt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(int.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsNum(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(decimal.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isobjid", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsObjId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = MModule.plainText(parser.CurrentState.Arguments["0"].Message);
		// Object ID format is #dbref:timestamp (e.g., #123:456789)
		var match = Regex.Match(arg, @"^#\d+:\d+$");
		return ValueTask.FromResult(new CallState(match.Success ? "1" : "0"));
	}

	[SharpFunction(Name = "isregexp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> isregexp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = parser.CurrentState.Arguments["0"].Message!.ToString();

		if (string.IsNullOrWhiteSpace(arg)) return ValueTask.FromResult<CallState>(new("0"));

		try { Regex.Match("", arg); } catch (ArgumentException) { return ValueTask.FromResult<CallState>(new("0")); }

		return ValueTask.FromResult<CallState>(new("1"));
	}

	[SharpFunction(Name = "isword", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = MModule.plainText(parser.CurrentState.Arguments["0"].Message);
		return ValueTask.FromResult(new CallState(Regex.IsMatch(str, @"^[a-zA-Z]$")));
	}

	[SharpFunction(Name = "itext", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement itext - requires text file system integration
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "letq", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
	public static async ValueTask<CallState> LetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		// TODO: Check if MarkupString is properly Immutable. If not, make it Immutable!
		var validPeek = parser.CurrentState.Registers.TryPeek(out var currentRegisters);
		var newRegisters = currentRegisters!.ToDictionary(k => k.Key, kv => kv.Value);
		parser.CurrentState.Registers.Push(newRegisters);

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;

		for (var i = 0; i < numberedArguments.Count - 1; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				numberedArguments[i.ToString()].Message!.ToString().ToUpper(),
				numberedArguments[(i + 1).ToString()].Message!);
		}

		if (everythingIsOkay)
		{
			var parsed = await parser.FunctionParse(numberedArguments.Last().Value.Message!);
			_ = parser.CurrentState.Registers.TryPop(out _);
			return parsed!;
		}

		_ = parser.CurrentState.Registers.TryPop(out _);
		return new CallState("#-1 REGISTER NAME INVALID");
	}

	[SharpFunction(Name = "link", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Link(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement link - requires exit linking
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "list", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> List(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		
		if (args.Count == 0)
		{
			return ValueTask.FromResult(CallState.Empty);
		}
		
		// Get all arguments as plain text
		var items = args.Values
			.Select(x => MModule.plainText(x.Message))
			.Where(x => !string.IsNullOrEmpty(x))
			.ToList();
		
		if (items.Count == 0)
		{
			return ValueTask.FromResult(CallState.Empty);
		}
		else if (items.Count == 1)
		{
			return ValueTask.FromResult(new CallState(items[0]));
		}
		else if (items.Count == 2)
		{
			return ValueTask.FromResult(new CallState($"{items[0]} and {items[1]}"));
		}
		else
		{
			// More than 2 items: "a, b, and c"
			var result = string.Join(", ", items.Take(items.Count - 1)) + ", and " + items.Last();
			return ValueTask.FromResult(new CallState(result));
		}
	}

	[SharpFunction(Name = "listq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		_ = parser.CurrentState.Registers.TryPeek(out var kv);
		return ValueTask.FromResult(new CallState(string.Join(" ", kv!.Keys)));
	}

	[SharpFunction(Name = "lset", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		
		// Get the list
		if (!args.TryGetValue("0", out var arg0))
		{
			return ValueTask.FromResult(CallState.Empty);
		}
		var listStr = arg0.Message!;
		
		// Get the position (1-based)
		if (!args.TryGetValue("1", out var arg1) || 
		    !int.TryParse(MModule.plainText(arg1.Message), out var position))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}
		
		// Get the new value
		if (!args.TryGetValue("2", out var arg2))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}
		var newValue = arg2.Message!;
		
		// Get optional input delimiter (default is space)
		var inputDelimiter = " ";
		if (args.TryGetValue("3", out var arg3))
		{
			inputDelimiter = MModule.plainText(arg3.Message);
		}
		
		// Get optional output delimiter (default is same as input)
		var outputDelimiter = inputDelimiter;
		if (args.TryGetValue("4", out var arg4))
		{
			outputDelimiter = MModule.plainText(arg4.Message);
		}
		
		// Split the list
		var items = MModule.split2(MModule.single(inputDelimiter), listStr).ToList();
		
		// Check if position is valid (1-based indexing)
		if (position < 1 || position > items.Count)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorArgRange));
		}
		
		// Set the item at the position (convert to 0-based)
		items[position - 1] = newValue;
		
		// Join back with output delimiter
		var result = string.Join(outputDelimiter, items.Select(x => MModule.plainText(x)));
		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "null", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular )]
	public static ValueTask<CallState> Null(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CallState.Empty);

	[SharpFunction(Name = "open", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Open(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement open - requires exit creation
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "r", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> R(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var registerName = MModule.plainText(args["0"].Message);
		
		// Try to get the register value from the current stack
		if (parser.CurrentState.Registers.TryPeek(out var registers))
		{
			if (registers.TryGetValue(registerName.ToUpper(), out var value))
			{
				return ValueTask.FromResult(new CallState(value));
			}
		}
		
		// If not found and there's a default value, return it
		if (args.Count == 2)
		{
			return ValueTask.FromResult(new CallState(args["1"].Message!));
		}
		
		// Otherwise return empty
		return ValueTask.FromResult(CallState.Empty);
	}
	[SharpFunction(Name = "rand", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Rand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var random = new Random();
		var args = parser.CurrentState.Arguments;
		
		// Check if first argument exists and is not empty
		if (!args.TryGetValue("0", out var arg0) || string.IsNullOrWhiteSpace(MModule.plainText(arg0.Message)))
		{
			// No arguments: random number between 0 and 2^31-1
			return ValueTask.FromResult(new CallState(random.Next(0, int.MaxValue)));
		}
		
		// Check if second argument exists and is not empty
		if (!args.TryGetValue("1", out var arg1) || string.IsNullOrWhiteSpace(MModule.plainText(arg1.Message)))
		{
			// One argument: random number from 0 to arg-1
			if (!int.TryParse(MModule.plainText(arg0.Message), out var maxVal) || maxVal <= 0)
			{
				return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
			}
			return ValueTask.FromResult(new CallState(random.Next(0, maxVal)));
		}
		
		// Two arguments: random number between min and max (inclusive)
		if (!int.TryParse(MModule.plainText(arg0.Message), out var minVal) ||
		    !int.TryParse(MModule.plainText(arg1.Message), out var maxVal2))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}
		if (minVal > maxVal2)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}
		// Next is exclusive of upper bound, so add 1
		return ValueTask.FromResult(new CallState(random.Next(minVal, maxVal2 + 1)));
	}

	[SharpFunction(Name = "registers", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Registers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		
		// Get current registers
		if (!parser.CurrentState.Registers.TryPeek(out var registers))
		{
			return ValueTask.FromResult(CallState.Empty);
		}
		
		// No arguments: return count of registers
		if (!args.TryGetValue("0", out var arg0))
		{
			return ValueTask.FromResult(new CallState(registers.Count));
		}
		
		// First argument determines what to return
		var mode = MModule.plainText(arg0.Message).ToLower();
		
		if (mode == "list" || mode == "names")
		{
			// Return space-separated list of register names
			return ValueTask.FromResult(new CallState(string.Join(" ", registers.Keys)));
		}
		else if (mode == "get")
		{
			// Get specific register value (second argument is register name)
			if (!args.TryGetValue("1", out var arg1))
			{
				return ValueTask.FromResult(CallState.Empty);
			}
			var regName = MModule.plainText(arg1.Message).ToUpper();
			if (registers.TryGetValue(regName, out var value))
			{
				return ValueTask.FromResult(new CallState(value));
			}
			return ValueTask.FromResult(CallState.Empty);
		}
		
		// Default: return count
		return ValueTask.FromResult(new CallState(registers.Count));
	}
	
	[SharpFunction(Name = "render", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Render(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement render - requires evaluating code from another object's perspective
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}
	
	[SharpFunction(Name = "s", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> S(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> (await parser.FunctionParse(parser.CurrentState.Arguments.Last().Value.Message!))!;

	[SharpFunction(Name = "scan", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Scan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement scan - requires object scanning/searching
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "setq", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;
		
		for (var i = 0; i < numberedArguments.Count; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				numberedArguments[i.ToString()].Message!.ToString().ToUpper(),
				numberedArguments[(i + 1).ToString()].Message!);
		}

		if (everythingIsOkay)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}
		else
		{
			return ValueTask.FromResult(new CallState("#-1 REGISTER NAME INVALID"));
		}
	}

	[SharpFunction(Name = "setr", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.EvenArgsOnly)]
	public static ValueTask<CallState> setr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;
		
		for (var i = 0; i < numberedArguments.Count; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				numberedArguments[$"{i}"].Message!.ToString().ToUpper(),
				numberedArguments[$"{i + 1}"].Message!);
		}

		if (everythingIsOkay)
		{
			return ValueTask.FromResult(new CallState(numberedArguments["1"].Message!));
		}
		else
		{
			return ValueTask.FromResult(new CallState("#-1 REGISTER NAME INVALID"));
		}
	}
	[SharpFunction(Name = "soundex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var val) 
			? val.Message!.ToPlainText().ToLowerInvariant() 
			: "soundex";

		return ValueTask.FromResult<CallState>(
			arg1 == "soundex" 
				? arg0.ToSoundex()
				: Errors.NotSupported);
	}

	[SharpFunction(Name = "soundslike", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments.TryGetValue("2", out var val) 
			? val.Message!.ToPlainText().ToLowerInvariant() 
			: "soundex";

		return ValueTask.FromResult<CallState>(
			arg2 == "soundex" 
				? arg0.HasTheSameSoundex(arg1)
				: Errors.NotSupported);
	}

	[SharpFunction(Name = "suggest", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Suggest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement suggest - requires fuzzy string matching/suggestion algorithm
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "slev", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SLev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Return the current parser function depth (stack level)
		return ValueTask.FromResult(new CallState(parser.CurrentState.ParserFunctionDepth ?? 0));
	}

	[SharpFunction(Name = "stext", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement stext - requires text file system integration
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "tel", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Tel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement tel - requires teleport/movement functionality
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "testlock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TestLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement testlock - requires lock service integration
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "textentries", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement textentries - requires text file system integration
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement textfile - requires text file system integration
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}

	[SharpFunction(Name = "unsetq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> UnSetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.Count == 0)
		{
			var canPeek = parser.CurrentState.Registers.TryPeek(out var peek);
			peek!.Clear();
		}
		else
		{
			var registers = MModule.plainText(parser.CurrentState.Arguments["0"].Message).Split(" ");
			foreach (var r in registers)
			{
				var canPeek = parser.CurrentState.Registers.TryPeek(out var peek);
				peek!.TryRemove(r);
			}
		}

		return ValueTask.FromResult<CallState>(new(string.Empty));
	}

	[SharpFunction(Name = "wipe", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Wipe(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Implement wipe - requires attribute wiping functionality
		return ValueTask.FromResult(new CallState(Errors.NotSupported));
	}
}