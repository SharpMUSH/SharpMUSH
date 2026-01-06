using System.Drawing;
using System.Text.RegularExpressions;
using DotNext;
using DotNext.Collections.Generic;
using MarkupString;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
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
		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var startingQuota = (int)Configuration!.CurrentValue.Limit.StartingQuota;
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
			new DBRef(trueLocation == -1 ? 1 : trueLocation),
			defaultHomeDbref,
			startingQuota));

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
			if (code.StartsWith("/#"))
			{
				// Handle background RGB color
				background = AnsiColor.NewRGB(ColorTranslator.FromHtml(code[2..].ToString()));
				continue;
			}
			if (code.StartsWith(['#']))
			{
				// Handle foreground RGB color
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
						foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 39));
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

	[SharpFunction(Name = "allof", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["value..."])]
	public static async ValueTask<CallState> AllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		
		// If single argument, split it by spaces and check each element
		if (args.Count == 1)
		{
			var singleArg = await parser.FunctionParse(args["0"].Message!);
			var elements = singleArg!.Message!.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			
			var allTrue = true;
			foreach (var element in elements)
			{
				if (string.IsNullOrEmpty(element) ||
						element == "0" ||
						element.StartsWith("#-1") ||
						element.Equals("false", StringComparison.OrdinalIgnoreCase))
				{
					allTrue = false;
					break;
				}
			}
			return new CallState(allTrue ? "1" : "0");
		}

		// Multi-argument case: parse each argument and check
		var allTruthy = true;
		foreach (var arg in args)
		{
			var result = await parser.FunctionParse(arg.Value.Message!);
			var resultStr = MModule.plainText(result!.Message).Trim();

			if (string.IsNullOrEmpty(resultStr) ||
					resultStr == "0" ||
					resultStr.StartsWith("#-1") ||
					resultStr.Equals("false", StringComparison.OrdinalIgnoreCase))
			{
				allTruthy = false;
				break;
			}
		}

		return new CallState(allTruthy ? "1" : "0");
	}

	[SharpFunction(Name = "atrlock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> AtrLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var attributeName = args["0"].Message!.ToPlainText();

		// If two args, second is the object to check on; otherwise use executor
		AnySharpObject targetObj;
		if (args.ContainsKey("1"))
		{
			var objectName = args["1"].Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, objectName, LocateFlags.All);

			if (!locateResult.IsAnySharpObject)
			{
				return locateResult.AsError;
			}
			targetObj = locateResult.AsSharpObject;
		}
		else
		{
			targetObj = executor;
		}

		// Get the attribute's lock (stored in attrname`lock attribute)
		var lockAttrName = $"{attributeName}`LOCK";
		var lockAttr = await AttributeService!.GetAttributeAsync(
			executor, targetObj, lockAttrName,
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		if (!lockAttr.IsAttribute)
		{
			return "0"; // No lock set means no restriction
		}

		// Evaluate the lock
		var lockString = lockAttr.AsAttribute.Last().Value.ToPlainText();
		if (string.IsNullOrWhiteSpace(lockString))
		{
			return "0";
		}

		var passes = LockService!.Evaluate(lockString, targetObj, executor);
		return new CallState(passes ? "1" : "0");
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

		var code = args["0"].Message!;

		if (!int.TryParse(MModule.plainText(args["1"].Message), out var iterations) || iterations <= 0)
		{
			return new CallState(Errors.ErrorNumbers);
		}

		var outputFormat = "ms";
		if (args.Count >= 3 && args.TryGetValue("2", out var formatArg))
		{
			outputFormat = MModule.plainText(formatArg.Message).ToLower();
		}

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		for (int i = 0; i < iterations; i++)
		{
			await parser.FunctionParse(code);
		}
		stopwatch.Stop();

		var elapsed = stopwatch.Elapsed.TotalMilliseconds;
		if (outputFormat == "s" || outputFormat == "seconds")
		{
			return new CallState((elapsed / 1000.0).ToString("F6"));
		}
		else
		{
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
	public static async ValueTask<CallState> Clone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var targetName = args["0"].Message!.ToPlainText();

		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var location = await Mediator!.Send(new GetObjectNodeQuery(defaultHomeDbref));

		if (location.IsNone || location.IsExit)
		{
			return Errors.ErrorInvalidRoom;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return Errors.ErrorPerm;
				}

				if (obj.IsPlayer)
				{
					return Errors.ErrorInvalidObjectType;
				}

				// Determine new name (arg 1 if provided)
				var newName = obj.Object().Name;
				if (args.ContainsKey("1") && !string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
				{
					newName = args["1"].Message!.ToPlainText();
				}

				DBRef cloneDbRef;
				var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

				// Create the appropriate object type
				if (obj.IsThing)
				{
					cloneDbRef = await Mediator!.Send(new CreateThingCommand(
						newName,
						await executor.Where(),
						owner,
						location.Known.AsContainer
					));
				}
				else if (obj.IsRoom)
				{
					cloneDbRef = await Mediator!.Send(new CreateRoomCommand(
						newName,
						owner
					));
				}
				else if (obj.IsExit)
				{
					var nameParts = newName.Split(";");
					cloneDbRef = await Mediator!.Send(new CreateExitCommand(
						nameParts[0],
						nameParts.Skip(1).ToArray(),
						await executor.Where(),
						owner
					));
				}
				else
				{
					return Errors.ErrorInvalidObjectType;
				}

				// Get the cloned object
				var clonedObjOptional = await Mediator!.Send(new GetObjectNodeQuery(cloneDbRef));
				var clonedObj = clonedObjOptional.WithoutNone();

				// Copy attributes (excluding system attributes)
				await foreach (var attr in obj.Object().Attributes.Value)
				{
					if (!attr.Name.StartsWith("_"))
					{
						await AttributeService!.SetAttributeAsync(executor, clonedObj,
							attr.Name, attr.Value);
					}
				}

				// Trigger OBJECT`CREATE event for the clone
				await EventService!.TriggerEventAsync(
					parser,
					"OBJECT`CREATE",
					executor.Object().DBRef,
					cloneDbRef.ToString(),
					obj.Object().DBRef.ToString()); // cloned-from

				return new CallState(cloneDbRef.ToString());
			}
		);
	}

	[SharpFunction(Name = "create", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Create(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var name = args["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var location = await Mediator!.Send(new GetObjectNodeQuery(defaultHomeDbref));

		if (location.IsNone || location.IsExit)
		{
			await NotifyService!.Notify(executor, "Default home location is invalid.");
			return new CallState(Errors.ErrorInvalidRoom);
		}

		if (!await ValidateService!.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			await NotifyService!.Notify(executor, "Invalid name for a thing.");
			return new CallState(Errors.ErrorBadObjectName);
		}

		var thing = await Mediator!.Send(new CreateThingCommand(name.ToPlainText(),
			await executor.Where(),
			await executor.Object()
				.Owner.WithCancellation(CancellationToken.None),
			location.Known.AsContainer));

		return new CallState(thing);
	}

	[SharpFunction(Name = "die", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Die(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!int.TryParse(MModule.plainText(args["0"].Message), out var count) || count < 0)
		{
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

	[SharpFunction(Name = "dig", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Dig(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var roomName = args["0"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(roomName))
		{
			return Errors.ErrorBadObjectName;
		}

		// Create the room
		var response = await Mediator!.Send(new CreateRoomCommand(
			roomName,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		// Return the room's dbref
		return new CallState(response.ToString());
	}

	[SharpFunction(Name = "fn", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var functionName = parser.CurrentState.Arguments["0"].Message!;

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
		var str = MModule.plainText(parser.CurrentState.Arguments["0"].Message);
		// itext() returns 1 if the argument is text (not a number), 0 otherwise
		// A string is "text" if it cannot be parsed as a number
		return ValueTask.FromResult(new CallState(!decimal.TryParse(str, out _) ? "1" : "0"));
	}

	[SharpFunction(Name = "letq", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
	public static async ValueTask<CallState> LetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		// Note: MarkupString should be immutable - verify this if register behavior issues occur
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
	public static async ValueTask<CallState> Link(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objectName = args["0"].Message!.ToPlainText();
		var destName = args["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async exitObj =>
			{
				if (!await PermissionService!.Controls(executor, exitObj))
				{
					return Errors.ErrorPerm;
				}

				// Handle different link types
				if (exitObj.IsExit)
				{
					// Check for special link types
					if (destName.Equals(LinkTypeHome, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService!.SetAttributeAsync(executor, exitObj, AttrLinkType, MModule.single(LinkTypeHome));
						return "1";
					}
					else if (destName.Equals(LinkTypeVariable, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService!.SetAttributeAsync(executor, exitObj, AttrLinkType, MModule.single(LinkTypeVariable));
						return "1";
					}

					// Link to a room
					return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (!destObj.IsRoom)
							{
								return Errors.ErrorInvalidDestination;
							}

							var destinationRoom = destObj.AsRoom;

							bool canLink = await PermissionService!.Controls(executor, destObj);

							if (!canLink)
							{
								var destFlags = await System.Linq.AsyncEnumerable.ToArrayAsync(destinationRoom.Object.Flags.Value);
								var hasLinkOk = destFlags.Any(f => f.Name.Equals("LINK_OK", StringComparison.OrdinalIgnoreCase));

								if (!hasLinkOk)
								{
									return Errors.ErrorPerm;
								}
							}

							await AttributeService!.SetAttributeAsync(executor, exitObj, AttrLinkType, MModule.empty());
							await Mediator!.Send(new LinkExitCommand(exitObj.AsExit, destinationRoom));

							return "1";
						}
					);
				}
				else if (exitObj.IsThing || exitObj.IsPlayer)
				{
					// Set home for thing or player
					return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (!destObj.IsRoom)
							{
								return Errors.ErrorInvalidDestination;
							}

							AnySharpContent contentObj = exitObj.IsThing ? exitObj.AsThing : (AnySharpContent)exitObj.AsPlayer;
							await Mediator!.Send(new SetObjectHomeCommand(contentObj, destObj.AsRoom));
							return "1";
						}
					);
				}
				else if (exitObj.IsRoom)
				{
					// Set drop-to for room
					return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (!destObj.IsRoom)
							{
								return Errors.ErrorInvalidDestination;
							}

							await Mediator!.Send(new LinkRoomCommand(exitObj.AsRoom, destObj.AsRoom));
							return "1";
						}
					);
				}

				return Errors.ErrorInvalidObjectType;
			});
	}

	[SharpFunction(Name = "list", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> List(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		if (args.Count == 0)
		{
			return CallState.Empty;
		}

		var option = args.TryGetValue("0", out var a0) ? MModule.plainText(a0.Message)!.Trim().ToLowerInvariant() : string.Empty;
		var type = args.TryGetValue("1", out var a1) ? MModule.plainText(a1.Message)!.Trim().ToLowerInvariant() : string.Empty;

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
				{
					var list = new List<string>();
					var attributes = Mediator!.CreateStream(new GetAllAttributeEntriesQuery());
					await foreach (var attr in attributes)
					{
						list.Add(attr.Name.ToLowerInvariant());
					}
					list.Sort(StringComparer.OrdinalIgnoreCase);
					return new CallState(JoinSpace(list));
				}
			case "locks":
				{
					var lockNames = Enum.GetNames(typeof(LockType))
						.Select(n => n.ToLowerInvariant())
						.OrderBy(x => x);
					return new CallState(JoinSpace(lockNames));
				}
			case "flags":
				{
					var list = new List<string>();
					var flags = Mediator!.CreateStream(new GetAllObjectFlagsQuery());
					await foreach (var f in flags)
					{
						list.Add(f.Name.ToLowerInvariant());
					}
					list.Sort(StringComparer.OrdinalIgnoreCase);
					return new CallState(JoinSpace(list));
				}
			case "powers":
				{
					var list = new List<string>();
					var powers = Mediator!.CreateStream(new GetPowersQuery());
					await foreach (var p in powers)
					{
						list.Add(p.Name.ToLowerInvariant());
					}
					list.Sort(StringComparer.OrdinalIgnoreCase);
					return new CallState(JoinSpace(list));
				}
			default:
				return CallState.Empty;
		}

		static async ValueTask<CallState> GetWizardMotdAsync(IMUSHCodeParser parser, string option)
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
			if (!(executor.IsGod() || await executor.IsWizard()))
			{
				return new CallState("#-1 PERMISSION DENIED");
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

	[SharpFunction(Name = "null", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Null(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CallState.Empty);

	[SharpFunction(Name = "open", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Open(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var exitName = args["0"].Message!.ToPlainText();

		// Parse exit name and aliases
		var exitParts = exitName.Split(";");
		var primaryName = exitParts[0];
		var aliases = exitParts.Skip(1).ToArray();

		// Get source location (default to executor's location)
		var sourceRoom = await executor.Where();

		// Optional: arg 1 could be destination, arg 2 could be source room
		// For now, keep it simple - create exit at executor's location

		// Check permissions
		if (!await PermissionService!.Controls(executor, sourceRoom.WithExitOption()))
		{
			return Errors.ErrorPerm;
		}

		// Create the exit
		var exitDbRef = await Mediator!.Send(new CreateExitCommand(
			primaryName,
			aliases,
			sourceRoom,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		return new CallState(exitDbRef.ToString());
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
		var args = parser.CurrentState.Arguments;

		// Check if first argument exists and is not empty
		if (!args.TryGetValue("0", out var arg0) || string.IsNullOrWhiteSpace(MModule.plainText(arg0.Message)))
		{
			// No arguments: random number between 0 and 2^31-1
			return ValueTask.FromResult(new CallState(Random.Shared.Next(0, int.MaxValue)));
		}

		// Check if second argument exists and is not empty
		if (!args.TryGetValue("1", out var arg1) || string.IsNullOrWhiteSpace(MModule.plainText(arg1.Message)))
		{
			// One argument: random number from 0 to arg-1
			if (!int.TryParse(MModule.plainText(arg0.Message), out var maxVal) || maxVal <= 0)
			{
				return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
			}
			return ValueTask.FromResult(new CallState(Random.Shared.Next(0, maxVal)));
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
		return ValueTask.FromResult(new CallState(Random.Shared.Next(minVal, maxVal2 + 1)));
	}

	[SharpFunction(Name = "registers", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Registers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		// Get current registers from the stack
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

		// Return space-separated list of register names
		if (mode == "list" || mode == "names")
		{
			return ValueTask.FromResult(new CallState(string.Join(" ", registers.Keys)));
		}

		// Get specific register value (second argument is register name)
		if (mode == "get")
		{
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
	public static async ValueTask<CallState> Render(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objectName = args["0"].Message!.ToPlainText();
		var code = args["1"].Message!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async obj =>
			{
				// Check permissions
				if (!await PermissionService!.Controls(executor, obj))
				{
					return Errors.ErrorPerm;
				}

				// Evaluate the code using the AttributeService which handles executor context properly
				// This evaluates the code from the perspective of the target object
				var result = await AttributeService!.EvaluateAttributeFunctionAsync(
					parser,
					obj, // executor is the target object
					code,
					[],
					evalParent: false,
					ignorePermissions: false,
					ignoreLambda: true);

				return new CallState(result);
			});
	}

	[SharpFunction(Name = "s", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> S(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> (await parser.FunctionParse(parser.CurrentState.Arguments.Last().Value.Message!))!;

	[SharpFunction(Name = "scan", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Scan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
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
			
			// Locate the looker object
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, lookerName, LocateFlags.All);
			if (locateResult.IsError || locateResult.IsNone)
			{
				return CallState.Empty;
			}
			looker = locateResult.AsAnyObject;
		}

		// Check permissions - must control looker
		if (!await PermissionService!.Controls(executor, looker))
		{
			return Errors.ErrorPerm;
		}

		// Collect objects to scan based on switches
		var objectsToScan = new List<AnySharpObject>();
		var switchList = switches.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		
		// Determine which locations to check
		bool checkMe = switchList.Contains("me") || switchList.Contains("all") || switchList.Contains("self");
		bool checkInventory = switchList.Contains("inventory") || switchList.Contains("all") || switchList.Contains("self");
		bool checkRoom = switchList.Contains("room") || switchList.Contains("all");
		bool checkGlobals = switchList.Contains("globals") || switchList.Contains("all");

		// Add looker itself
		if (checkMe)
		{
			objectsToScan.Add(looker);
		}

		// Add looker's inventory
		if (checkInventory && looker.IsContainer)
		{
			var inventory = await System.Linq.AsyncEnumerable.ToListAsync(
				Mediator!.CreateStream(new GetContentsQuery(looker.AsContainer)));
			foreach (var item in inventory)
			{
				objectsToScan.Add(item.WithRoomOption());
			}
		}

		// Add looker's location and its contents
		if (checkRoom)
		{
			var dbref = looker.Object().DBRef;
			var locationQuery = new GetLocationQuery(dbref);
			var locationOpt = await Mediator!.Send(locationQuery);
			
			if (!locationOpt.IsNone)
			{
				var location = locationOpt.WithoutNone();
				objectsToScan.Add(location.WithExitOption());
				
				// Add contents of the location
				var contents = await System.Linq.AsyncEnumerable.ToListAsync(
					Mediator!.CreateStream(new GetContentsQuery(location)));
				foreach (var item in contents)
				{
					objectsToScan.Add(item.WithRoomOption());
				}
			}
		}

		// Add Master Room objects
		if (checkGlobals)
		{
			var masterRoomDbref = new DBRef(0);
			var masterRoomResult = await Mediator!.Send(new GetObjectNodeQuery(masterRoomDbref));
			if (!masterRoomResult.IsNone)
			{
				var masterRoom = masterRoomResult.Known;
				objectsToScan.Add(masterRoom);
				
				if (masterRoom.IsContainer)
				{
					var masterContents = await System.Linq.AsyncEnumerable.ToListAsync(
						Mediator!.CreateStream(new GetContentsQuery(masterRoom.AsContainer)));
					foreach (var item in masterContents)
					{
						objectsToScan.Add(item.WithRoomOption());
					}
				}
			}
		}

		// Remove duplicates
		var uniqueObjects = objectsToScan
			.Distinct()
			.ToAsyncEnumerable();

		// Use CommandDiscoveryService to find matching $-commands
		var matchResult = await CommandDiscoveryService!.MatchUserDefinedCommand(
			parser,
			uniqueObjects,
			command);

		if (matchResult.IsNone())
		{
			return CallState.Empty;
		}

		// Format results as "dbref/attribute" pairs
		var matches = matchResult.AsValue();
		var results = matches.Select(match => 
			$"{match.SObject.Object().DBRef}/{match.Attribute.Name}");

		return string.Join(" ", results);
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
	public static async ValueTask<CallState> Suggest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
				return new CallState(Errors.ErrorIntegers);
			}
		}

		// Get suggestion data from expanded server data
		var suggestionData = await ObjectDataService!.GetExpandedServerDataAsync<SuggestionData>();
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
		var suggestions = vocabulary
			.Select(v => new { Word = v, Distance = CalculateLevenshteinDistance(word.ToLower(), v.ToLower()) })
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

		var sourceLength = source.Length;
		var targetLength = target.Length;
		var distance = new int[sourceLength + 1, targetLength + 1];

		// Initialize first column and row
		for (var i = 0; i <= sourceLength; i++)
		{
			distance[i, 0] = i;
		}

		for (var j = 0; j <= targetLength; j++)
		{
			distance[0, j] = j;
		}

		// Calculate distances
		for (var i = 1; i <= sourceLength; i++)
		{
			for (var j = 1; j <= targetLength; j++)
			{
				var cost = source[i - 1] == target[j - 1] ? 0 : 1;

				distance[i, j] = Math.Min(
					Math.Min(
						distance[i - 1, j] + 1,      // Deletion
						distance[i, j - 1] + 1),     // Insertion
					distance[i - 1, j - 1] + cost);  // Substitution
			}
		}

		return distance[sourceLength, targetLength];
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
	public static async ValueTask<CallState> Tel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objectName = args["0"].Message!.ToPlainText();
		var destName = args["1"].Message!.ToPlainText();

		// Optional quiet flag (arg 2) and force flag (arg 3) - for now we ignore these

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async targetObj =>
			{
				if (targetObj.IsRoom)
				{
					return Errors.ErrorCannotTeleport;
				}

				if (!await PermissionService!.Controls(executor, targetObj))
				{
					return Errors.ErrorCannotTeleport;
				}

				return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, destName, LocateFlags.All,
					async destObj =>
					{
						if (destObj.IsExit)
						{
							return Errors.ErrorInvalidDestination;
						}

						var destinationContainer = destObj.AsContainer;
						var targetContent = targetObj.AsContent;

						// Check for containment loops
						if (await MoveService!.WouldCreateLoop(targetContent, destinationContainer))
						{
							return "#-1 WOULD CREATE LOOP";
						}

						// Move the object
						await Mediator!.Send(new MoveObjectCommand(
							targetContent,
							destinationContainer,
							executor.Object().DBRef,
							true, // silent
							"tel()"));

						return "1";
					});
			});
	}

	[SharpFunction(Name = "testlock", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> TestLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Two forms:
		// testlock(<lock key>, <victim>) - test a lock expression against a victim
		// testlock(<object>, <victim>, <lock name>) - test a named lock on object against victim
		
		if (args.Count == 2)
		{
			// Form 1: testlock(<lock key>, <victim>)
			var lockString = args["0"].Message!.ToPlainText();
			var victimName = args["1"].Message!.ToPlainText();
			
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, victimName, LocateFlags.All,
				victim =>
				{
					// Validate the lock string
					if (!LockService!.Validate(lockString, executor))
					{
						return ValueTask.FromResult(new CallState("#-1 INVALID LOCK"));
					}

					// Evaluate the lock: does victim pass the lock expression?
					var passes = LockService.Evaluate(lockString, executor, victim);
					return ValueTask.FromResult(new CallState(passes ? "1" : "0"));
				});
		}
		else
		{
			// Form 2: testlock(<object>, <victim>, <lock name>)
			var objectName = args["0"].Message!.ToPlainText();
			var victimName = args["1"].Message!.ToPlainText();
			var lockName = args["2"].Message!.ToPlainText();
			
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, objectName, LocateFlags.All,
				async lockedObject =>
				{
					// Locate the victim
					var victimResult = await LocateService!.Locate(parser, executor, executor, victimName, LocateFlags.All);
					if (!victimResult.IsValid())
					{
						return new CallState("#-1 INVALID VICTIM");
					}
					var victim = victimResult.AsAnyObject;
					
					// Get the named lock from the object
					if (!lockedObject.Object().Locks.TryGetValue(lockName, out var lockData))
					{
						// No lock set means it passes
						return new CallState("1");
					}

					// Evaluate the lock: does victim pass this lock?
					var passes = LockService!.Evaluate(lockData.LockString, lockedObject, victim);
					return new CallState(passes ? "1" : "0");
				});
		}
	}

	[SharpFunction(Name = "textentries", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var separator = args.TryGetValue("1", out var sep) 
			? sep.Message!.ToPlainText() 
			: " ";

		if (TextFileService == null)
		{
			return new CallState("#-1 TEXT FILE SERVICE NOT AVAILABLE");
		}

		try
		{
			var entries = await TextFileService.ListEntriesAsync(fileReference, separator);
			return new CallState(entries);
		}
		catch (FileNotFoundException)
		{
			return new CallState("#-1 FILE NOT FOUND");
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error in textentries({File})", fileReference);
			return new CallState("#-1 ERROR");
		}
	}

	[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var entryName = args["1"].Message!.ToPlainText();

		if (TextFileService == null)
		{
			return new CallState("#-1 TEXT FILE SERVICE NOT AVAILABLE");
		}

		try
		{
			var content = await TextFileService.GetEntryAsync(fileReference, entryName);
			return content != null 
				? new CallState(content)
				: new CallState("#-1 ENTRY NOT FOUND");
		}
		catch (FileNotFoundException)
		{
			return new CallState("#-1 FILE NOT FOUND");
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error in textfile({File}, {Entry})", fileReference, entryName);
			return new CallState("#-1 ERROR");
		}
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
	public static async ValueTask<CallState> Wipe(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objectName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return Errors.ErrorPerm;
				}

				// Collect all non-system attributes (those not starting with _)
				var attributesToClear = new List<string>();
				await foreach (var attr in obj.Object().Attributes.Value)
				{
					if (!attr.Name.StartsWith("_"))
					{
						attributesToClear.Add(attr.Name);
					}
				}

				// Clear each attribute
				foreach (var attrName in attributesToClear)
				{
					await Mediator!.Send(new ClearAttributeCommand(obj.Object().DBRef, [attrName]));
				}

				return $"Wiped {attributesToClear.Count}";
			});
	}
}
