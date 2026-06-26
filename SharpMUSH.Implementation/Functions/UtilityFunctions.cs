using DotNext;
using DotNext.Collections.Generic;
using ANSILibrary;
using MarkupString;
using MarkupString.MarkupImplementation;
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
using System.Drawing;
using System.Text.RegularExpressions;
using static ANSILibrary.ANSI;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// This is not directly compatible with functions that expect just a DBREF (#1234).
	// Consider adding a configuration option for backward compatibility mode.
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

		// TODO: Move ANSI color processing to AnsiMarkup module for better integration.
		// This would allow align() and other markup functions to work directly with parsed ANSI structures.
		AnsiColor foreground = AnsiColor.NoAnsi.Instance;
		AnsiColor background = AnsiColor.NoAnsi.Instance;
		var blink = false;
		var bold = false;
		var clear = false;
		var invert = false;
		var underline = false;

		var ansiCodes = args["0"].Message!.ToString().Split(' ');
		Func<bool, byte, byte[]> highlightFunc = (highlight, b) => highlight ? [1, b] : [b];
		var colorsConfig = ColorConfiguration?.CurrentValue;

		foreach (var cde in ansiCodes)
		{
			var code = cde.AsSpan();
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
				var color = new AnsiColor.RGB(ColorTranslator.FromHtml(code.ToString()));
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
					var color = new AnsiColor.RGB(ColorTranslator.FromHtml(hexColor));
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
					var color = new AnsiColor.RGB(ColorTranslator.FromHtml(hexColor));
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
					var color = new AnsiColor.RGB(Color.FromArgb(r, g, b));
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
						foreground = AnsiColor.NoAnsi.Instance;
						background = AnsiColor.NoAnsi.Instance;
						blink = false;
						bold = false;
						invert = false;
						underline = false;
						curHilight = false;
						break;
					case 'd':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 39));
						break;
					case 'x':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 30));
						break;
					case 'r':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 31));
						break;
					case 'g':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 32));
						break;
					case 'y':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 33));
						break;
					case 'b':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 34));
						break;
					case 'm':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 35));
						break;
					case 'c':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 36));
						break;
					case 'w':
						foreground = StringExtensions.AnsiBytes(highlightFunc(curHilight, 37));
						break;
					case 'D':
						background = StringExtensions.AnsiByte(49);
						break;
					case 'X':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 40));
						break;
					case 'R':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 41));
						break;
					case 'G':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 42));
						break;
					case 'Y':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 43));
						break;
					case 'B':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 44));
						break;
					case 'M':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 45));
						break;
					case 'C':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 46));
						break;
					case 'W':
						background = StringExtensions.AnsiBytes(highlightFunc(curHilight, 47));
						break;
					default:
						// Do nothing. Just skip.
						// Should probably warn about invalid ansi codes.
						break;
				}
			}
		}

		var details = new AnsiStructure
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

		return ValueTask.FromResult(new CallState(MModule.MarkupSingle2(new Ansi(details), args["1"].Message ?? MModule.Empty())));
	}

	[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> AtAt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(string.Empty));

	[SharpFunction(Name = "allof", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["value..."])]
	public static async ValueTask<CallState> AllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		var delimiter = MModule.plainText(delimParsed!.Message);

		var truthyValues = new List<string>();
		for (var i = 0; i < args.Count - 1; i++)
		{
			var parsed = await parser.FunctionParse(args[i.ToString()].Message!);
			var value = MModule.plainText(parsed!.Message);
			// Truthy: non-empty, not "0", not starting with "#-"
			if (!string.IsNullOrEmpty(value) &&
				value != "0" &&
				!value.StartsWith("#-"))
			{
				truthyValues.Add(value);
			}
		}

		return new CallState(string.Join(delimiter, truthyValues));
	}

	[SharpFunction(Name = "atrlock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> AtrLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var attributeName = args["0"].Message!.ToPlainText();

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
			return new CallState(ErrorMessages.Returns.Numbers);
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
			await NotifyService!.NotifyLocalized(parser.CurrentState.Executor!.Value, nameof(ErrorMessages.Notifications.CantSeeThat));
			return new CallState(ErrorMessages.Returns.NoSuchPlayer);
		}

		var dbRef = dbRefConversion.AsValue();
		var objectInfo = await Mediator!.Send(new GetObjectNodeQuery(dbRef));
		if (!objectInfo.IsPlayer)
		{
			return new CallState(ErrorMessages.Returns.NoSuchPlayer);
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
			return ErrorMessages.Returns.InvalidRoom;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				if (obj.IsPlayer)
				{
					return ErrorMessages.Returns.InvalidObjectType;
				}

				var newName = obj.Object().Name;
				if (args.ContainsKey("1") && !string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
				{
					newName = args["1"].Message!.ToPlainText();
				}

				DBRef cloneDbRef;
				var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

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
					return ErrorMessages.Returns.InvalidObjectType;
				}

				var clonedObjOptional = await Mediator!.Send(new GetObjectNodeQuery(cloneDbRef));
				var clonedObj = clonedObjOptional.WithoutNone();

				var preserve = args.ContainsKey("3") &&
					args["3"].Message!.ToPlainText().Equals("preserve", StringComparison.OrdinalIgnoreCase);

				await foreach (var attr in obj.Object().Attributes.Value)
				{
					if (!attr.Name.StartsWith("_"))
					{
						await AttributeService!.SetAttributeAsync(executor, clonedObj,
							attr.Name, attr.Value);
					}
				}

				await foreach (var flag in obj.Object().Flags.Value)
				{
					if (preserve || (!flag.Name.Contains("WIZARD") && !flag.Name.Contains("ROYALTY")))
					{
						await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, clonedObj, flag.Name, false);
					}
				}

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
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DefaultHomeLocationInvalid), executor);
			return new CallState(ErrorMessages.Returns.InvalidRoom);
		}

		if (!await ValidateService!.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidNameThing), executor);
			return new CallState(ErrorMessages.Returns.BadObjectName);
		}

		var thing = await Mediator!.Send(new CreateThingCommand(name.ToPlainText(),
			await executor.Where(),
			await executor.Object()
				.Owner.WithCancellation(CancellationToken.None),
			location.Known.AsContainer));

		return new CallState($"#{thing.Number}");
	}

	[SharpFunction(Name = "die", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Die(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!int.TryParse(MModule.plainText(args["0"].Message), out var count) || count < 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}
		if (!int.TryParse(MModule.plainText(args["1"].Message), out var sides) || sides <= 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		// Optional third argument for how many rolls to show (vs just return sum)
		var showCount = count;
		if (args.Count == 3)
		{
			if (!int.TryParse(MModule.plainText(args["2"].Message), out showCount) || showCount < 0)
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

	[SharpFunction(Name = "dig", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Dig(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var roomName = args["0"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(roomName))
		{
			return ErrorMessages.Returns.BadObjectName;
		}

		var response = await Mediator!.Send(new CreateRoomCommand(
			roomName,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		return new CallState(response.ToString());
	}

	[SharpFunction(Name = "fn", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var functionName = MModule.plainText(parser.CurrentState.Arguments["0"].Message);
		if (string.IsNullOrWhiteSpace(functionName))
		{
			return new CallState("#-1 FUNCTION (No function name given)");
		}

		if (parser.FunctionLibrary.TryGetValue(functionName.ToLower(), out _))
		{
			// Build function call string and re-parse: fn(add,1,2) -> add(1,2)
			var fnArgs = parser.CurrentState.ArgumentsOrdered
				.Skip(1)
				.Select(x => MModule.plainText(x.Value.Message));
			var callString = $"{functionName}({string.Join(",", fnArgs)})";
			var result = await parser.FunctionParse(MModule.single(callString));
			return result ?? CallState.Empty;
		}

		// Fall back to user-defined attribute function
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var result2 = await AttributeService!.EvaluateAttributeFunctionAsync(
			parser,
			executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);

		// If attribute lookup returned nothing, report function not found
		if (result2 == null || MModule.plainText(result2).Length == 0)
		{
			return new CallState($"#-1 FUNCTION ({functionName.ToUpper()}) NOT FOUND");
		}

		return new CallState(result2);
	}
	[SharpFunction(Name = "functions", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> FFunctions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var functionLibrary = parser.FunctionLibrary;

		var pattern = "*";
		if (parser.CurrentState.Arguments.TryGetValue("0", out var arg0))
		{
			var patternArg = MModule.plainText(arg0.Message);
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
			var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
			filteredFunctions = allFunctions.Where(name => regex.IsMatch(name));
		}

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
		var match = ObjIdRegex().Match(arg);
		return ValueTask.FromResult(new CallState(match.Success ? "1" : "0"));
	}

	[SharpFunction(Name = "isregexp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> isregexp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = parser.CurrentState.Arguments["0"].Message!.ToString();

		if (string.IsNullOrWhiteSpace(arg)) return ValueTask.FromResult<CallState>(new("0"));

		// Validate regex by attempting to construct it with timeout to prevent ReDoS
		// Use a helper method to avoid exception-based control flow
		var isValid = IsValidRegexPattern(arg);
		return ValueTask.FromResult<CallState>(new(isValid ? "1" : "0"));
	}

	private static bool IsValidRegexPattern(string pattern)
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
	public static ValueTask<CallState> IsWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = MModule.plainText(parser.CurrentState.Arguments["0"].Message);
		return ValueTask.FromResult(new CallState(IsWordRegex().IsMatch(str)));
	}

	[SharpFunction(Name = "itext", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var levelArg = args["0"].Message!.ToPlainText();
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (levelArg.Equals("L", StringComparison.OrdinalIgnoreCase))
		{
			if (maxCount == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
			}
			return ValueTask.FromResult(new CallState(parser.CurrentState.IterationRegisters.Last().Value));
		}

		if (!int.TryParse(levelArg, out var level))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		if (level < 0 || level >= maxCount)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
		}

		// Iteration registers are stored innermost-first (stack top = current iteration),
		// so level 0 = current (top), level 1 = parent, etc. requires reverse indexing.
		var value = parser.CurrentState.IterationRegisters.ElementAt(maxCount - level - 1).Value;
		return ValueTask.FromResult(new CallState(value));
	}

	[SharpFunction(Name = "letq", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
	public static async ValueTask<CallState> LetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;
		var npairs = (numberedArguments.Count - 1) / 2;

		// If no pairs, just evaluate the body in the current scope (PE_REGS_LET with empty scope:
		// all register writes pass up to the caller, nothing is saved/restored).
		if (npairs == 0)
		{
			return (await parser.FunctionParse(numberedArguments.Last().Value.Message!))!;
		}

		// Note: MarkupString should be immutable - verify this if register behavior issues occur
		var validPeek = parser.CurrentState.Registers.TryPeek(out var currentRegisters);
		var newRegisters = currentRegisters!.ToDictionary(k => k.Key, kv => kv.Value);
		parser.CurrentState.Registers.Push(newRegisters);

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
		return new CallState(ErrorMessages.Returns.BadRegName);
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
					return ErrorMessages.Returns.PermissionDenied;
				}

				if (exitObj.IsExit)
				{
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
								return ErrorMessages.Returns.InvalidDestination;
							}

							var destinationRoom = destObj.AsRoom;

							bool canLink = await PermissionService!.Controls(executor, destObj);

							if (!canLink)
							{
								var destFlags = await System.Linq.AsyncEnumerable.ToArrayAsync(destinationRoom.Object.Flags.Value);
								var hasLinkOk = destFlags.Any(f => f.Name.Equals("LINK_OK", StringComparison.OrdinalIgnoreCase));

								if (!hasLinkOk)
								{
									return ErrorMessages.Returns.PermissionDenied;
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
								return ErrorMessages.Returns.InvalidDestination;
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
								return ErrorMessages.Returns.InvalidDestination;
							}

							await Mediator!.Send(new LinkRoomCommand(exitObj.AsRoom, destObj.AsRoom));
							return "1";
						}
					);
				}

				return ErrorMessages.Returns.InvalidObjectType;
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

		if (!args.TryGetValue("0", out var arg0))
		{
			return ValueTask.FromResult(CallState.Empty);
		}
		var listStr = arg0.Message!;

		if (!args.TryGetValue("1", out var arg1) ||
				!int.TryParse(MModule.plainText(arg1.Message), out var position))
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
			inputDelimiter = MModule.plainText(arg3.Message);
		}

		var outputDelimiter = inputDelimiter;
		if (args.TryGetValue("4", out var arg4))
		{
			outputDelimiter = MModule.plainText(arg4.Message);
		}

		var items = MModule.splitList(MModule.single(inputDelimiter), listStr).ToList();

		if (position < 1 || position > items.Count)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));
		}

		// Set the item at the position (convert to 0-based)
		items[position - 1] = newValue;

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
			return ErrorMessages.Returns.PermissionDenied;
		}

		// Create the exit
		var exitDbRef = await Mediator!.Send(new CreateExitCommand(
			primaryName,
			aliases,
			sourceRoom,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		return new CallState(exitDbRef.ToString());
	}

	// r(<register>[, <type>]) — read a register. <type> (default "qregisters") selects the store, per
	// `help r`: qregisters (setq/setr), args (the %0-%9 stack + named regexp $-command captures), iter
	// (itext context), switch (stext context), regexp (re*() capture names, %$0 and named). Note: the
	// second argument is the TYPE selector, NOT a fallback default value.
	[SharpFunction(Name = "r", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> R(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var registerName = MModule.plainText(args["0"].Message);
		var typeArgStr = args.TryGetValue("1", out var typeArg) && typeArg.Message is not null
			? MModule.plainText(typeArg.Message).Trim()
			: string.Empty;

		// <type> defaults to qregisters and accepts unambiguous PREFIXES (e.g. "a"→args, "q"→qregisters,
		// "sw"→switch). The five type names have distinct first letters, so any prefix matches at most one.
		var canonicalType = string.IsNullOrEmpty(typeArgStr)
			? "qregisters"
			: new[] { "qregisters", "args", "iter", "switch", "regexp" }
				.FirstOrDefault(t => t.StartsWith(typeArgStr, StringComparison.OrdinalIgnoreCase));
		if (canonicalType is null)
			return ValueTask.FromResult(new CallState($"#-1 R: INVALID REGISTER TYPE '{typeArgStr}'"));

		switch (canonicalType)
		{
			// setq()/setr() registers (names are case-insensitive).
			case "qregisters":
				return ValueTask.FromResult(
					parser.CurrentState.Registers.TryPeek(out var qregs)
					&& qregs.TryGetValue(registerName.ToUpper(), out var qval)
						? new CallState(qval)
						: CallState.Empty);

			// The argument stack (%0-%9, up to 30) plus named stack registers from regexp $-commands.
			case "args":
				return ValueTask.FromResult(
					parser.CurrentState.EnvironmentRegisters.TryGetValue(registerName, out var aval)
						? new CallState(aval.Message!)
						: CallState.Empty);

			// %$0 and named captures from re*() regexp functions.
			case "regexp":
				return ValueTask.FromResult(
					parser.CurrentState.RegexRegisters.TryPeek(out var rxregs)
					&& rxregs.TryGetValue(registerName, out var rxval)
						? new CallState(rxval)
						: CallState.Empty);

			// itext() context — int level, or "L" for the outermost iteration.
			case "iter":
			{
				var maxCount = parser.CurrentState.IterationRegisters.Count;
				if (registerName.Equals("L", StringComparison.OrdinalIgnoreCase))
					return ValueTask.FromResult(maxCount == 0
						? new CallState(ErrorMessages.Returns.RegisterRange)
						: new CallState(parser.CurrentState.IterationRegisters.Last().Value));
				if (!int.TryParse(registerName, out var lvl))
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
				if (lvl < 0 || lvl >= maxCount)
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
				return ValueTask.FromResult(
					new CallState(parser.CurrentState.IterationRegisters.ElementAt(maxCount - lvl - 1).Value));
			}

			// stext() context — int depth, or "L" for the outermost switch.
			case "switch":
			{
				var stack = parser.CurrentState.SwitchStack;
				var depth = 0;
				if (registerName.Equals("L", StringComparison.OrdinalIgnoreCase))
					depth = stack.Count - 1;
				else if (!string.IsNullOrEmpty(registerName) && (!int.TryParse(registerName, out depth) || depth < 0))
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NonNegativeInteger));
				if (stack.Count == 0 || depth < 0 || depth >= stack.Count)
					return ValueTask.FromResult(new CallState(string.Empty));
				return ValueTask.FromResult(new CallState(stack.ElementAtOrDefault(depth) ?? MModule.empty()));
			}

			default:
				// Unreachable: canonicalType is always one of the five valid names (or we returned above).
				return ValueTask.FromResult(new CallState($"#-1 R: INVALID REGISTER TYPE '{typeArgStr}'"));
		}
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
			if (!int.TryParse(MModule.plainText(arg0.Message), out var maxVal))
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
		if (!int.TryParse(MModule.plainText(arg0.Message), out var minVal) ||
				!int.TryParse(MModule.plainText(arg1.Message), out var maxVal2))
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
				if (!await PermissionService!.Controls(executor, obj))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				// Evaluates the code from the perspective of the target object
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

			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, lookerName, LocateFlags.All);
			if (locateResult.IsError || locateResult.IsNone)
			{
				return CallState.Empty;
			}
			looker = locateResult.AsAnyObject;
		}

		if (!await PermissionService!.Controls(executor, looker))
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

		if (checkInventory && looker.IsContainer)
		{
			var inventory = await System.Linq.AsyncEnumerable.ToListAsync(
				Mediator!.CreateStream(new GetContentsQuery(looker.AsContainer)));
			foreach (var item in inventory)
			{
				objectsToScan.Add(item.WithRoomOption());
			}
		}

		if (checkRoom)
		{
			var dbref = looker.Object().DBRef;
			var locationQuery = new GetLocationQuery(dbref);
			var locationOpt = await Mediator!.Send(locationQuery);

			if (!locationOpt.IsNone)
			{
				var location = locationOpt.WithoutNone();
				objectsToScan.Add(location.WithExitOption());

				var contents = await System.Linq.AsyncEnumerable.ToListAsync(
					Mediator!.CreateStream(new GetContentsQuery(location)));
				foreach (var item in contents)
				{
					objectsToScan.Add(item.WithRoomOption());
				}
			}
		}

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

		var uniqueObjects = objectsToScan
			.Distinct()
			.ToAsyncEnumerable();

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
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.BadRegName));
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
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.BadRegName));
		}
	}
	[SharpFunction(Name = "soundex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	public static ValueTask<CallState> SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	private static string ComputeSoundex(string input)
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
	private static string ComputePhoneticHash(string input)
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
				return new CallState(ErrorMessages.Returns.Integers);
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
		return ValueTask.FromResult(new CallState(parser.CurrentState.SwitchStack.Count));
	}

	[SharpFunction(Name = "stext", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// stext([<n>]) returns the string being matched in the current or nth nested switch.
		// stext(L) returns the outermost switch string.
		// n=0 is current switch, n=1 is the switch the current is nested in, etc.

		var args = parser.CurrentState.Arguments;
		var stack = parser.CurrentState.SwitchStack;

		int depth = 0;

		// Validate arguments first, before checking stack count
		if (args.TryGetValue("0", out var depthArg) && depthArg.Message != null)
		{
			var depthStr = depthArg.Message!.ToPlainText().Trim();

			// Skip processing if the argument is empty (defaults to 0)
			if (!string.IsNullOrEmpty(depthStr))
			{
				// Handle "L" or "l" for outermost (last) switch
				if (depthStr.Equals("L", StringComparison.OrdinalIgnoreCase))
				{
					depth = stack.Count - 1;
				}
				else if (!int.TryParse(depthStr, out depth) || depth < 0)
				{
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NonNegativeInteger));
				}
			}
		}

		if (stack.Count == 0)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		// Convert depth to index from top of stack
		// depth 0 = current (top), depth 1 = parent, etc.
		if (depth >= stack.Count)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		var item = stack.ElementAtOrDefault(depth);
		return ValueTask.FromResult(new CallState(item ?? MModule.empty()));
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
					return ErrorMessages.Returns.CannotTeleport;
				}

				if (!await PermissionService!.Controls(executor, targetObj))
				{
					return ErrorMessages.Returns.CannotTeleport;
				}

				return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, destName, LocateFlags.All,
					async destObj =>
					{
						if (destObj.IsExit)
						{
							return ErrorMessages.Returns.InvalidDestination;
						}

						var destinationContainer = destObj.AsContainer;
						var targetContent = targetObj.AsContent;

						if (await MoveService!.WouldCreateLoop(targetContent, destinationContainer))
						{
							return ErrorMessages.Returns.WouldCreateLoop;
						}

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

	[SharpFunction(Name = "testlock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> TestLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// PennMUSH: testlock(<lock key>, <victim>) - test a lock expression against a victim
		var lockString = args["0"].Message!.ToPlainText();
		var victimName = args["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, victimName, LocateFlags.All,
			victim =>
			{
				if (!LockService!.Validate(lockString, executor))
				{
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidLock));
				}

				// Evaluate the lock: does victim pass the lock expression?
				var passes = LockService.Evaluate(lockString, executor, victim);
				return ValueTask.FromResult(new CallState(passes ? "1" : "0"));
			});
	}

	[SharpFunction(Name = "textentries", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	public static async ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
					return ErrorMessages.Returns.PermissionDenied;
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

				foreach (var attrName in attributesToClear)
				{
					await Mediator!.Send(new ClearAttributeCommand(obj.Object().DBRef, [attrName]));
				}

				return $"Wiped {attributesToClear.Count}";
			});
	}

	[GeneratedRegex(@"^#\d+:\d+$")]
	private static partial Regex ObjIdRegex();

	[GeneratedRegex(@"^[a-zA-Z]$")]
	private static partial Regex IsWordRegex();
}
