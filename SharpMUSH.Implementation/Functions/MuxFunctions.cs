using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using CommandSyntax = SharpMUSH.Library.Services.CommandDiscoveryService;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// Functions TinyMUX games call that PennMUSH has no name for. Behaviour follows TinyMUX's
/// documentation; the side-effect ones run the command they stand for, so they are refused and
/// reported exactly as typing that command would be.
/// </summary>
public partial class Functions
{
	/// <summary>Seconds since 1970 of the new moon of 6 January 2000, 18:14 UTC.</summary>
	private const double NewMoonEpoch = 947182440.0;

	/// <summary>The mean synodic month, in seconds.</summary>
	private const double SynodicMonth = 29.53059 * 86400.0;

	/// <summary>TinyMUX's limit on how many numbers <c>lrand()</c> returns.</summary>
	private const int MaxRandomCount = 8000;

	/// <summary>TinyMUX's limits for <c>distribute()</c>.</summary>
	private const int MaxDistributePoints = 1_000_000;

	private const int MaxDistributeBins = 2000;

	/// <summary>TinyMUX's limit on <c>lrooms()</c> depth.</summary>
	private const int MaxRoomDepth = 50;

	/// <summary>How far up the parent chain <c>lrooms()</c> looks for exits.</summary>
	private const int MaxRoomParentDepth = 10;

	[SharpFunction(Name = "trigger", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged,
		ParameterNames = ["object/attribute", "arguments..."])]
	public async ValueTask<CallState> TriggerFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Compatibility(parser).MuxQueueFunctions) return new CallState(ErrorMessages.Returns.FunctionDisabled);

		// TinyMUX runs @trigger quietly here: the function says nothing on success.
		return await RunCommandFromFunctionAsync(parser, "@TRIGGER", ["QUIET"],
			parser.CurrentState.ArgumentsOrdered.Values.Select(arg => arg.Message ?? MarkupText.Empty));
	}

	[SharpFunction(Name = "verb", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged,
		ParameterNames = ["victim", "actor", "what", "whatd", "owhat", "owhatd", "awhat", "arguments..."])]
	public async ValueTask<CallState> VerbFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Compatibility(parser).MuxQueueFunctions) return new CallState(ErrorMessages.Returns.FunctionDisabled);

		var args = parser.CurrentState.ArgumentsOrdered.Values.Select(arg => arg.Message ?? MarkupText.Empty).ToList();

		// The actor defaults to the enactor.
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var enactorName = MarkupText.Plain($"#{enactor.Object().DBRef.Number}");
		if (args.Count < 2) args.Add(enactorName);
		else if (args[1].ToPlainText().Length == 0) args[1] = enactorName;

		return await RunCommandFromFunctionAsync(parser, "@VERB", [], args);
	}

	[SharpFunction(Name = "destroy", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public ValueTask<CallState> DestroyFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> RunCommandFromFunctionAsync(parser, "@DESTROY", [], [parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty]);

	/// <summary>
	/// Runs <paramref name="command"/> as the executor would by typing it, with the function's arguments in
	/// the command's argument slots. The command tells the executor what it tells them; the function
	/// returns nothing, or the permission error when the executor may not use the command.
	/// </summary>
	private async ValueTask<CallState> RunCommandFromFunctionAsync(IMUSHCodeParser parser, string command,
		string[] switches, IEnumerable<MString> arguments)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await parser.ServiceProvider.GetRequiredService<ICommandPermission>().CanRunAsync(executor, command))
		{
			return PermissionRefusal.CannotUse(command).Return;
		}

		var commandParser = parser.Push(parser.CurrentState with
		{
			Command = command,
			Switches = switches,
			Arguments = arguments
				.Select((argument, index) => (Key: index.ToString(CultureInfo.InvariantCulture), Value: new CallState(argument)))
				.ToDictionary(pair => pair.Key, pair => pair.Value),
			Function = null
		});

		await commandParser.CommandLibrary[command].LibraryInformation.Command.Invoke(commandParser);
		return CallState.Empty;
	}

	[SharpFunction(Name = "strip", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "characters"])]
	public ValueTask<CallState> Strip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var text = args["0"].Message?.ToPlainText() ?? string.Empty;
		var remove = args.TryGetValue("1", out var removeArg) ? removeArg.Message?.ToPlainText() ?? string.Empty : string.Empty;

		// With nothing to remove, the string comes back without its markup.
		return ValueTask.FromResult(new CallState(remove.Length == 0
			? text
			: string.Concat(text.Where(c => !remove.Contains(c)))));
	}

	[SharpFunction(Name = "lrand", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["lower", "upper", "count", "osep"])]
	public ValueTask<CallState> ListRandom(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var lower = ArgHelpers.ParseInteger(args["0"].Message?.ToPlainText());
		var upper = ArgHelpers.ParseInteger(args["1"].Message?.ToPlainText());
		var count = Math.Min(ArgHelpers.ParseInteger(args["2"].Message?.ToPlainText()), MaxRandomCount);
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Space);

		if (count < 1 || lower > upper) return ValueTask.FromResult(CallState.Empty);

		var numbers = Enumerable.Range(0, count)
			.Select(_ => MarkupText.Plain(Random.Shared.NextInt64(lower, (long)upper + 1).ToString(CultureInfo.InvariantCulture)));
		return ValueTask.FromResult(new CallState(MarkupText.Join(separator, numbers)));
	}

	[SharpFunction(Name = "moon", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["seconds", "numeric"])]
	public ValueTask<CallState> Moon(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var secondsText = args.TryGetValue("0", out var secondsArg) ? secondsArg.Message?.ToPlainText() ?? string.Empty : string.Empty;

		double seconds;
		if (secondsText.Length == 0)
		{
			seconds = parser.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
		}
		else if (!ArgHelpers.TryNumber(parser, secondsText, out seconds))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Number));
		}

		var numeric = args.TryGetValue("1", out var numericArg) && numericArg.Message.Truthy(parser);
		return ValueTask.FromResult(new CallState(MoonPhase(seconds, numeric)));
	}

	/// <summary>
	/// The moon's phase at <paramref name="seconds"/>: how far into the synodic month the time falls, and
	/// how much of the disc is lit, rounded to a whole percent.
	/// </summary>
	private static string MoonPhase(double seconds, bool numeric)
	{
		var phase = (seconds - NewMoonEpoch) % SynodicMonth;
		if (phase < 0) phase += SynodicMonth;

		var fraction = phase / SynodicMonth;
		var lit = (int)((1.0 - Math.Cos(2.0 * Math.PI * fraction)) / 2.0 * 100.0 + 0.5);
		var waxing = fraction < 0.5;

		if (numeric) return (waxing ? lit : -lit).ToString(CultureInfo.InvariantCulture);

		var direction = waxing ? "Waxing" : "Waning";
		return lit switch
		{
			>= 100 => "The Moon is Full",
			<= 0 => "The Moon is New",
			>= 49 and <= 51 => $"The Moon is {direction} Half ({lit}% of Full)",
			> 50 => $"The Moon is {direction} Gibbous ({lit}% of Full)",
			_ => $"The Moon is {direction} Crescent ({lit}% of Full)"
		};
	}

	[SharpFunction(Name = "bittype", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public async ValueTask<CallState> BitType(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? string.Empty;
		if (name.Length == 0) name = "me";

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, name, LocateFlags.All,
			async found => new CallState(await PrivilegeLevelAsync(parser,
				new AnySharpObject(await found.Object().Owner.WithCancellation(CancellationToken.None)))));
	}

	/// <summary>
	/// RhostMUSH's privilege scale, one step for each of these roles, highest first. A step is reached by holding a
	/// role whose priority is at least the lowest of its roles', so a game's own roles fall in by where they rank.
	/// Below the lowest step is 1, and a guest is 0.
	/// </summary>
	private static readonly (int Level, string[] Roles)[] BitTypeSteps =
	[
		(6, [BuiltInRoles.ImmortalSlug]),
		(5, [BuiltInRoles.WizardSlug]),
		(4, [BuiltInRoles.RoyaltySlug, BuiltInRoles.CouncilorSlug]),
		(3, [BuiltInRoles.BuilderSlug, BuiltInRoles.ArchitectSlug]),
		(2, ["helper", "guildmaster"])
	];

	/// <summary>
	/// The step <paramref name="owner"/> reaches on <see cref="BitTypeSteps"/>, from the highest priority of the roles
	/// it holds: 7 for God, 0 for a guest.
	/// </summary>
	private static async ValueTask<int> PrivilegeLevelAsync(IMUSHCodeParser parser, AnySharpObject owner)
	{
		if (owner.IsGod()) return 7;
		if (await owner.IsGuest()) return 0;

		var held = (await owner.GrantsAsync(ExecutionBudget.CurrentToken)).Roles.Max(role => (int?)role.Role.Priority) ?? 0;
		var roles = parser.ServiceProvider.GetRequiredService<IRoleRegistryService>();
		foreach (var (level, slugs) in BitTypeSteps)
		{
			if (held >= await StepPriorityAsync(roles, slugs)) return level;
		}

		return 1;
	}

	/// <summary>
	/// The lowest priority of the roles of <paramref name="slugs"/> the world defines or, when it defines none of them,
	/// of the built-in ones.
	/// </summary>
	private static async ValueTask<int> StepPriorityAsync(IRoleRegistryService roles, string[] slugs)
	{
		var priorities = new List<int>();
		foreach (var slug in slugs)
		{
			if (await roles.GetRoleAsync(slug, ExecutionBudget.CurrentToken) is SharpRole role) priorities.Add(role.Priority);
		}

		return priorities.Count > 0
			? priorities.Min()
			: BuiltInRoles.All.Concat(BuiltInRoles.Starters).Concat(BuiltInRoles.RhostLevels)
				.Where(role => slugs.Contains(role.Slug, StringComparer.OrdinalIgnoreCase))
				.Min(role => role.Priority);
	}

	[SharpFunction(Name = "between", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["low", "high", "value", "inclusive"])]
	public ValueTask<CallState> Between(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		if (!ArgHelpers.TryNumber(parser, args["0"].Message?.ToPlainText(), out var low)
			|| !ArgHelpers.TryNumber(parser, args["1"].Message?.ToPlainText(), out var high)
			|| !ArgHelpers.TryNumber(parser, args["2"].Message?.ToPlainText(), out var value))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		var inclusive = args.TryGetValue("3", out var inclusiveArg) && inclusiveArg.Message.Truthy(parser);
		return ValueTask.FromResult(new CallState(inclusive
			? low <= value && value <= high
			: low < value && value < high));
	}

	[SharpFunction(Name = "choose", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["items", "weights", "delimiter"])]
	public ValueTask<CallState> Choose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var items = MushText.SplitList(ArgHelpers.Delimiter(args, 2), args["0"].Message ?? MarkupText.Empty);
		var weights = MushText.SplitList(MarkupText.Space, args["1"].Message ?? MarkupText.Empty)
			.Select(weight => Math.Max(0L, ArgHelpers.ParseInteger(weight.ToPlainText())))
			.ToArray();

		if (items.Length != weights.Length) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ListsMustBeEqualSize));

		var total = weights.Sum();
		if (total == 0) return ValueTask.FromResult(CallState.Empty);

		var roll = Random.Shared.NextInt64(total);
		for (var i = 0; i < items.Length; i++)
		{
			if (roll < weights[i]) return ValueTask.FromResult(new CallState(items[i]));
			roll -= weights[i];
		}

		return ValueTask.FromResult(CallState.Empty);
	}

	[SharpFunction(Name = "distribute", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["points", "bins", "osep"])]
	public ValueTask<CallState> Distribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (!ArgHelpers.TryInteger(parser, args["0"].Message?.ToPlainText(), out var points)
			|| !ArgHelpers.TryInteger(parser, args["1"].Message?.ToPlainText(), out var binCount))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integers));
		}

		if (points is < 0 or > MaxDistributePoints || binCount is < 1 or > MaxDistributeBins)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NumberOutOfRange));
		}

		var bins = new int[binCount];
		for (var point = 0; point < points; point++)
		{
			bins[Random.Shared.Next(binCount)]++;
		}

		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		return ValueTask.FromResult(new CallState(MarkupText.Join(separator,
			bins.Select(bin => MarkupText.Plain(bin.ToString(CultureInfo.InvariantCulture))))));
	}

	[SharpFunction(Name = "roman", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["number"])]
	public ValueTask<CallState> Roman(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!ArgHelpers.TryStrictInteger(parser.CurrentState.Arguments["0"].Message?.ToPlainText().Trim(), out int number))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		if (number is < 1 or > 3999) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NumberOutOfRange));

		string[] thousands = ["", "M", "MM", "MMM"];
		string[] hundreds = ["", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM"];
		string[] tens = ["", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC"];
		string[] ones = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];

		return ValueTask.FromResult(new CallState(
			thousands[number / 1000] + hundreds[number / 100 % 10] + tens[number / 10 % 10] + ones[number % 10]));
	}

	[SharpFunction(Name = "crc32", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["strings..."])]
	public ValueTask<CallState> Crc32(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// The strings are checksummed one after another, so crc32(a,b) is crc32(ab).
		var crc = 0xFFFFFFFFu;
		foreach (var argument in parser.CurrentState.ArgumentsOrdered.Values)
		{
			foreach (var b in Encoding.UTF8.GetBytes(argument.Message?.ToPlainText() ?? string.Empty))
			{
				crc ^= b;
				for (var bit = 0; bit < 8; bit++)
				{
					crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
				}
			}
		}

		return ValueTask.FromResult(new CallState((~crc).ToString(CultureInfo.InvariantCulture)));
	}

	[SharpFunction(Name = "strmem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> StringMemory(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(
			Encoding.UTF8.GetByteCount(parser.CurrentState.Arguments["0"].Message?.ToPlainText() ?? string.Empty)));

	[SharpFunction(Name = "ledit", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "find", "replace", "delimiter", "osep"])]
	public ValueTask<CallState> ListEdit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = ArgHelpers.Delimiter(args, 3);
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);
		var find = MushText.SplitList(delimiter, args["1"].Message ?? MarkupText.Empty).Select(x => x.ToPlainText()).ToList();
		var replace = MushText.SplitList(delimiter, args["2"].Message ?? MarkupText.Empty);

		// The first match wins; a find word past the end of the replacements becomes empty.
		var edited = MushText.SplitList(delimiter, args["0"].Message ?? MarkupText.Empty)
			.Select(item => find.IndexOf(item.ToPlainText()) switch
			{
				-1 => item,
				var index when index < replace.Length => replace[index],
				_ => MarkupText.Empty
			});

		return ValueTask.FromResult(new CallState(MarkupText.Join(separator, edited)));
	}

	[SharpFunction(Name = "lattrcmds", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object/pattern"])]
	public ValueTask<CallState> ListCommandAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> CommandAttributesAsync(parser, attributes => string.Join(" ", attributes
			.Where(attribute => CommandSyntax.CommandPatternRegex().IsMatch(attribute.Value.ToPlainText()))
			.Select(attribute => attribute.LongName)));

	[SharpFunction(Name = "lcmds", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object/pattern", "osep", "type"])]
	public ValueTask<CallState> ListCommands(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Space);
		var listens = args.Count > 2 && args["2"].Message?.ToPlainText() is ['^', ..];
		var pattern = listens ? CommandSyntax.ListenPatternRegex() : CommandSyntax.CommandPatternRegex();

		// Patterns come back in lower case, as TinyMUX matches them without regard to case.
		return CommandAttributesAsync(parser, attributes => MarkupText.Join(separator, attributes
			.Select(attribute => pattern.Match(attribute.Value.ToPlainText()))
			.Where(match => match.Success)
			.Select(match => MarkupText.Plain(
				CommandSyntax.UnescapePatternSeparator(match.Groups["pattern"].Value).ToLowerInvariant()))));
	}

	/// <summary>The attributes on an object that the executor may read, matching an optional wildcard.</summary>
	private async ValueTask<CallState> CommandAttributesAsync(IMUSHCodeParser parser, Func<SharpAttribute[], CallState> project)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (HelperFunctions.SplitDbRefAndOptionalAttr(parser.CurrentState.Arguments["0"].Message?.ToPlainText() ?? string.Empty)
			is not { Object: var name, Attribute: var attributePattern })
		{
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, name, LocateFlags.All,
			async found => await AttributeService.GetAttributePatternAsync(executor, found, attributePattern ?? "*", false,
					IAttributeService.AttributePatternMode.Wildcard) switch
				{
					SharpAttribute[] attributes => project(attributes),
					Error<string> error => new CallState(error.Value)
				});
	}

	[SharpFunction(Name = "lrooms", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["room", "depth", "all"])]
	public async ValueTask<CallState> ListRooms(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		var depth = 1;
		if (args.TryGetValue("1", out var depthArg) && !ArgHelpers.TryInteger(parser, depthArg.Message?.ToPlainText(), out depth))
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		if (depth is < 0 or > MaxRoomDepth) return new CallState(ErrorMessages.Returns.NumberOutOfRange);

		var all = !args.TryGetValue("2", out var allArg) || allArg.Message.Truthy(parser);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message?.ToPlainText() ?? string.Empty, LocateFlags.All,
			async room => room.IsRoom
				? await RoomsWithinAsync(parser, executor, enactor, room, depth, all)
				: new CallState(ErrorMessages.Returns.NotARoom));
	}

	/// <summary>
	/// Walks out from <paramref name="start"/> through the exits the executor can see, on the room and its
	/// parents, one step per level. A room is listed when it is at the last level (or at any level, with
	/// <paramref name="all"/>) and the executor can examine it, stands in it, or the enactor is it.
	/// </summary>
	private async ValueTask<CallState> RoomsWithinAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject enactor, AnySharpObject start, int depth, bool all)
	{
		var reality = parser.ServiceProvider.GetRequiredService<IRealityPolicy>();
		var executorLocation = (await executor.Where()).Object().DBRef.Number;
		var seen = new HashSet<int> { start.Object().DBRef.Number };
		var reported = new SortedSet<int>();
		var level = new List<AnySharpObject> { start };

		for (var step = 1; step <= depth && level.Count > 0; step++)
		{
			var next = new List<AnySharpObject>();
			foreach (var room in level)
			{
				foreach (var destination in await ExitDestinationsAsync(executor, room, reality))
				{
					var number = destination.Object().DBRef.Number;
					if (!seen.Add(number)) continue;

					next.Add(destination);
					if ((all || step == depth)
						&& (number == executorLocation || number == enactor.Object().DBRef.Number
							|| await PermissionService.CanExamine(executor, destination)))
					{
						reported.Add(number);
					}
				}
			}

			level = next;
		}

		return new CallState(string.Join(" ", reported.Select(number => $"#{number}")));
	}

	/// <summary>The rooms the exits the viewer can see on <paramref name="room"/> and its parents lead to.</summary>
	private async ValueTask<List<AnySharpObject>> ExitDestinationsAsync(AnySharpObject viewer, AnySharpObject room,
		IRealityPolicy reality)
	{
		var destinations = new List<AnySharpObject>();
		var holder = room;
		for (var parentDepth = 0; parentDepth <= MaxRoomParentDepth; parentDepth++)
		{
			if (holder.AsOptionalContainer is AnySharpContainer container)
			{
				var canSee = await WorldVisibility.CreateScanAsync(viewer, holder, reality, ConnectionService,
					ExecutionBudget.CurrentToken);
				await foreach (var item in container.Content(Mediator).WithCancellation(ExecutionBudget.CurrentToken))
				{
					if (item is not SharpExit exit || !await canSee(item, ExecutionBudget.CurrentToken)) continue;
					if (await exit.Home.WithCancellation(CancellationToken.None) is AnySharpContainer and SharpRoom destination)
					{
						destinations.Add(new AnySharpObject(destination));
					}
				}
			}

			if (await holder.Object().Parent.WithCancellation(CancellationToken.None) is not AnySharpObject parent) break;
			holder = parent;
		}

		return destinations;
	}
}
