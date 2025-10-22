using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "ACCNAME", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> AccName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FOLDERSTATS", MinArgs = 0, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> folderstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "pidinfo", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ALIAS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Alias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FINDABLE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Findable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FULLALIAS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> FullAlias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FULLNAME", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> FullName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "GETPIDS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> GetPIDs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "powers", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Powers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASPOWER", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> HasPower(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var toLocate = parser.CurrentState.Arguments["0"].Message!;
		var power = MModule.plainText(parser.CurrentState.Arguments["1"].Message).ToUpper();
		var maybeLocate = await
			LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, toLocate.ToPlainText(),
				LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var located = maybeLocate.AsSharpObject;
		var hasPower = await located.HasPower(power);

		return new CallState(hasPower);
	}

	[SharpFunction(Name = "HASTYPE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> HasType(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var toLocate = parser.CurrentState.Arguments["0"].Message!;
		var typeQuery = MModule.plainText(parser.CurrentState.Arguments["1"].Message).ToUpper().Split(" ");
		var maybeLocate = await
			LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, toLocate.ToPlainText(),
				LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		if (typeQuery.Any(x => x is not "PLAYER" and not "THING" and not "ROOM" and not "EXIT"))
		{
			return new CallState("#-1 NO SUCH TYPE.");
		}

		var located = maybeLocate.AsSharpObject;
		var hasType = typeQuery.Any(validType => located.HasType(validType));

		return new CallState(hasType);
	}

	[SharpFunction(Name = "INAME", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LPIDS", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> LPIDs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;
		var target = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, "me");
		var queueTypes = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, "wait semaphore").ToPlainText()
			.ToUpperInvariant()
			.Split(" ").Distinct();

		if (queueTypes.Any(type => type is not "WAIT" and not "SEMAPHORE" and not "INDEPENDENT"))
		{
			return new CallState("#-1 INVALID QUEUE TYPE.");
		}

		var maybeLocate = await
			LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, target.ToPlainText(),
				LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var located = maybeLocate.AsSharpObject;

		// TODO: Implement WAIT and INDEPENDENT queue handling
		var semaphorePids = await Mediator!
			.Send(new ScheduleSemaphoreQuery(located.Object().DBRef));

		var pids = await semaphorePids.Select<SemaphoreTaskData, string>(x => x.Pid.ToString())
			.ToArrayAsync();

		return new CallState(string.Join(' ', pids));
	}

	[SharpFunction(Name = "LSTATS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MONEY", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Money(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "mudname", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> MudName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Configuration!.CurrentValue.Net.MudName);

	[SharpFunction(Name = "mudurl", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> MudURL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Configuration!.CurrentValue.Net.MudUrl ?? "");

	[SharpFunction(Name = "name", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Name(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		//   name(<object>[, <new name>])
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var newName = parser.CurrentState.Arguments.GetValueOrDefault("1");

		if (newName is null)
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, 
				executor, executor, obj, LocateFlags.All,
				found => found.Object().Name);
		}

		if (Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, obj, LocateFlags.All,
				async found =>
					await ManipulateSharpObjectService!.SetName(executor, found, newName.Message!, true));
		}

		await NotifyService!.Notify(executor, Errors.ErrorNoSideFx);
		return false;
	}

	[SharpFunction(Name = "MONIKER", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Moniker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NEARBY", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Nearby(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PLAYERMEM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> PlayerMem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(0);

	[SharpFunction(Name = "QUOTA", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Quota(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "type", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Type(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All, found => found.TypeString());
	}

	[SharpFunction(Name = "TEXTSEARCH", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "COLORS", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Colors(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			return string.Join(" ", ColorConfiguration!.CurrentValue.Colors
				.Select(x => x.name));
		}

		if (args.Count == 1)
		{
			return string.Join(" ", ColorConfiguration!.CurrentValue.Colors
				.Where(x => x.name.Contains(args["0"].Message!.ToPlainText()))
				.Select(x => x.name));
		}

		// Colors , Format
		// TODO: Handle the various other functions.
		var colors = args["0"].Message!.ToPlainText().Split(" ");


		/*
  colors()
  colors(<wildcard>)
  colors(<colors>, <format>)

  With no arguments, colors() returns an unsorted, space-separated list of colors that PennMUSH knows the name of. You can use these colors in ansi(+<colorname>,text). The colors "xterm0" to "xterm255" are not included in the list, but can also be used in ansi().

  With one argument, returns an unsorted, space-separated list of colors that match the wildcard pattern <wildcard>.

  With two arguments, colors() returns information about specific colors. <colors> can be any string accepted by the ansi() function's first argument. <format> must be one of:

   hex, x:      return a hexcode in the format #rrggbb.
   rgb, r:      return the RGB components as a list (0 0 0 - 255 255 255)
   xterm256, d: return the number of the xterm color closest to the given <color>.
   xterm256x,h: return the number of the xterm color in base 16.
   16color, c:  return the letter of the closest ANSI color code (possibly including 'h' for highlight fg colors).
   name:     return a list of names of all the colors exactly matching the given colors, or '#-1 NO MATCHING COLOR NAME' if there is no exact match with a named color.
   auto:     returns the colors in the same format(s) they were given in.

  It can be used for working out how certain colors will downgrade to people using clients which aren't fully color-capable.

  <format> can also include the word "styles", in which case all ANSI styling options (f, u, i and h) present in <colors> are included in the output.

  See 'help colors2' for examples.
		*/
		throw new NotImplementedException();
	}
}