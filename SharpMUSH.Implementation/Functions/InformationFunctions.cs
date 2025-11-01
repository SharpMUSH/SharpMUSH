using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "accname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> AccName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "folderstats", MinArgs = 0, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> folderstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		AnySharpObject targetPlayer = executor;
		string? folderSpec = null;

		// Parse arguments - can be (), (folder), (player), or (player, folder)
		if (args.Count == 0)
		{
			// Use current folder
			folderSpec = await Implementation.Commands.MailCommand.MessageListHelper.CurrentMailFolder(
				parser, ObjectDataService!, executor);
		}
		else if (args.Count == 1)
		{
			var arg = args["0"].Message!.ToPlainText()!;
			
			// Try to determine if it's a folder number/name or a player
			if (int.TryParse(arg, out _) || arg.All(c => char.IsDigit(c) || char.IsUpper(c)))
			{
				// Looks like a folder
				folderSpec = arg;
			}
			else
			{
				// Must be wizard to view other player's mail
				if (!(executor.IsGod() || await executor.IsWizard()))
				{
					return new CallState("#-1 PERMISSION DENIED");
				}

				// Try to locate as player
				var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
					parser, executor, executor, arg, LocateFlags.PlayersPreference);
				
				if (locateResult.IsError || locateResult.IsNone)
				{
					// Not a player, try as folder name
					folderSpec = arg;
				}
				else
				{
					targetPlayer = locateResult.AsPlayer;
					folderSpec = await Implementation.Commands.MailCommand.MessageListHelper.CurrentMailFolder(
						parser, ObjectDataService!, targetPlayer);
				}
			}
		}
		else if (args.Count == 2)
		{
			// Must be wizard to view other player's mail
			if (!(executor.IsGod() || await executor.IsWizard()))
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			var playerArg = args["0"].Message!.ToPlainText()!;
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);
			
			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState("#-1 NO SUCH PLAYER");
			}

			targetPlayer = locateResult.AsPlayer;
			folderSpec = args["1"].Message!.ToPlainText()!;
		}

		// Get mail for the folder
		var folderMail = await Mediator!.Send(new GetMailListQuery(targetPlayer.AsPlayer, folderSpec ?? "INBOX"));
		var mailArray = await folderMail.ToArrayAsync();
		
		var read = mailArray.Count(m => m.Read);
		var unread = mailArray.Count(m => !m.Read);
		var cleared = mailArray.Count(m => m.Cleared);

		return new CallState($"{read} {unread} {cleared}");
	}


	[SharpFunction(Name = "restarts", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> restarts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return uptimeData?.Reboots ?? 0;
	}

	[SharpFunction(Name = "restarttime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> RestartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return (uptimeData?.LastRebootTime ?? DateTimeOffset.Now).ToUnixTimeMilliseconds();
	}

	[SharpFunction(Name = "pidinfo", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "alias", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Alias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "findable", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Findable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "fullalias", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> FullAlias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found =>
			{
				return ValueTask.FromResult(new CallState(
					string.Join(" ", found switch
					{
						{ IsExit: true, AsExit: var exit } => exit.Aliases ?? [],
						{ IsThing: true, AsThing: var thing } => thing.Aliases ?? [],
						{ IsPlayer: true, AsPlayer: var player } => player.Aliases ?? [],
						{ IsRoom: true, AsRoom: var room } => room.Aliases ?? [],
						_ => throw new ArgumentOutOfRangeException()
					})));
			});
	}

	[SharpFunction(Name = "fullname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> FullName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found =>
			{
				var name = found.Object().Name;
				return ValueTask.FromResult(new CallState(
					string.Join(" ", found switch
					{
						{ IsExit: true, AsExit: var exit } => [name, ..exit.Aliases ?? []],
						_ => [name]
					})));
			});
	}

	[SharpFunction(Name = "getpids", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> GetProcessIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		var split = HelperFunctions.SplitDbRefAndOptionalAttr(arg0);
		if (split.IsT1)
		{
			return string.Format(Errors.ErrorBadArgumentFormat, "getpids");
		}

		var (db, attr) = split.AsT0;

		if (attr is null)
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, db, LocateFlags.All,
				async found =>
				{
					var queryResult = await Mediator!.Send(new ScheduleSemaphoreQuery(found.Object().DBRef));
					var pids = queryResult.Select(x => MModule.single(x.Pid.ToString()));
					return MModule.multipleWithDelimiter(MModule.single(" "), await pids.ToArrayAsync());
				});
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, db, LocateFlags.All,
			async found =>
			{
				var dbAttr = new DbRefAttribute(found.Object().DBRef, attr.Split("`"));
				var queryResult = await Mediator!.Send(new ScheduleSemaphoreQuery(dbAttr));
				var pids = queryResult.Select(x => MModule.single(x.Pid.ToString()));
				return MModule.multipleWithDelimiter(MModule.single(" "), await pids.ToArrayAsync());
			});
	}

	[SharpFunction(Name = "powers", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Powers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var obj);
		parser.CurrentState.Arguments.TryGetValue("1", out var power);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		switch (parser.CurrentState.Arguments.Count)
		{
			case 0:
			{
				var allPowers = (await Mediator!.Send(new GetPowersQuery()))
					.Select(x => MModule.single(x.Name));
				return MModule.multipleWithDelimiter(MModule.single(" "), await allPowers.ToArrayAsync());
			}

			case 1:
				return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, obj!.Message!.ToPlainText(), LocateFlags.All,
					async found => MModule.multipleWithDelimiter(MModule.single(" "),
						await found.Object()
							.Powers.Value
							.Select(x => MModule.single(x.Name)).ToArrayAsync()));

			default:
			{
				if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
				{
					return Errors.ErrorNoSideFx;
				}

				return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, obj!.Message!.ToPlainText(), LocateFlags.All,
					async found =>
						await ManipulateSharpObjectService!.SetPower(executor, found, power!.Message!.ToPlainText(), true));
			}
		}
	}

	[SharpFunction(Name = "haspower", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "hastype", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "iname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "lpids", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "lstats", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "money", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "moniker", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Moniker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nearby", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Nearby(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "playermem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> PlayerMem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(0);

	[SharpFunction(Name = "quota", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "textsearch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "colors", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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