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
	public static async ValueTask<CallState> AccName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// accname() returns the accented name (ACCNAME attribute or name with ANSI)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				// Try to get the ACCNAME attribute
				var accnameAttr = await AttributeService!.GetAttributeAsync(
					executor, found, "ACCNAME", IAttributeService.AttributeMode.Read);

				if (accnameAttr.IsAttribute)
				{
					var attr = accnameAttr.AsAttribute.Last();
					var attrValue = attr.Value.ToString();
					if (!string.IsNullOrWhiteSpace(attrValue))
					{
						return new CallState(attrValue);
					}
				}

				// Fall back to name
				return new CallState(found.Object().Name);
			});
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
			// Folders are typically numbers (0-15) or uppercase names (INBOX)
			if (int.TryParse(arg, out var folderNum) && folderNum >= 0 && folderNum <= 15)
			{
				// Looks like a folder number
				folderSpec = arg;
			}
			else if (arg.All(char.IsUpper) || arg.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
			{
				// Looks like a folder name (all uppercase)
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
					// Not a player, try as folder name anyway
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
		var folderMail = Mediator!.CreateStream(new GetMailListQuery(targetPlayer.AsPlayer, folderSpec ?? "INBOX"));
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
	public static async ValueTask<CallState> PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// pidinfo() returns information about a process ID
		// Format: pidinfo(<pid>[, <field>][, <delimiter>])
		var args = parser.CurrentState.Arguments;
		var pidStr = args["0"].Message!.ToPlainText();
		
		if (!int.TryParse(pidStr, out var pid))
		{
			return new CallState("#-1 INVALID PID");
		}

		// TODO: Implement actual PID tracking and information retrieval
		// This requires integration with the queue/process management system
		// For now, return placeholder indicating not implemented
		await ValueTask.CompletedTask;
		return new CallState("#-1 NO SUCH PID");
	}

	[SharpFunction(Name = "alias", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Alias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var args = parser.CurrentState.Arguments;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found =>
			{
				var aliases = found switch
				{
					{ IsExit: true, AsExit: var exit } => exit.Aliases,
					{ IsThing: true, AsThing: var thing } => thing.Aliases,
					{ IsPlayer: true, AsPlayer: var player } => player.Aliases,
					{ IsRoom: true, AsRoom: var room } => room.Aliases,
					_ => null
				};

				// If no second argument, return first alias or empty string
				if (args.Count == 1)
				{
					return ValueTask.FromResult(new CallState(aliases?.FirstOrDefault() ?? string.Empty));
				}

				// With second argument, try to get the Nth alias (1-indexed)
				var indexArg = args["1"].Message!.ToPlainText();
				if (!int.TryParse(indexArg, out var index) || index < 1)
				{
					return ValueTask.FromResult(new CallState("#-1 INVALID ALIAS INDEX"));
				}

				return ValueTask.FromResult(new CallState(
					aliases != null && index <= aliases.Length
						? aliases[index - 1]
						: string.Empty));
			});
	}

	[SharpFunction(Name = "findable", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Findable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// findable() checks if the first object can find the second object
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var lookerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var targetArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		var maybeLooker = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, lookerArg, LocateFlags.All);

		if (maybeLooker.IsError)
		{
			return maybeLooker.AsError;
		}

		var looker = maybeLooker.AsSharpObject;

		// Try to locate the target from the looker's perspective
		var maybeTarget = await LocateService.Locate(parser, looker, executor, targetArg, LocateFlags.All);

		// If we can locate it, it's findable
		return new CallState(maybeTarget.IsValid());
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
					var queryResult = Mediator!.CreateStream(new ScheduleSemaphoreQuery(found.Object().DBRef));
					var pids = queryResult.Select(x => MModule.single(x.Pid.ToString()));
					return MModule.multipleWithDelimiter(MModule.single(" "), await pids.ToArrayAsync());
				});
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, db, LocateFlags.All,
			async found =>
			{
				var dbAttr = new DbRefAttribute(found.Object().DBRef, attr.Split("`"));
				var queryResult = Mediator!.CreateStream(new ScheduleSemaphoreQuery(dbAttr));
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
				var allPowers = (Mediator!.CreateStream(new GetPowersQuery()))
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
	public static async ValueTask<CallState> IName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// iname() returns the initial/internal name (name without ANSI codes)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found => ValueTask.FromResult(new CallState(MModule.plainText(MModule.single(found.Object().Name)))));
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
		var semaphorePids = Mediator!
			.CreateStream(new ScheduleSemaphoreQuery(located.Object().DBRef));

		var pids = await semaphorePids
			.Select<SemaphoreTaskData, string>(x => x.Pid.ToString())
			.ToArrayAsync();

		return new CallState(string.Join(' ', pids));
	}

	[SharpFunction(Name = "lstats", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> LStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lstats() returns statistics about objects in the database
		// For now, return placeholder values until database iteration is implemented
		await ValueTask.CompletedTask;
		
		// TODO: Implement database-wide object counting
		// Format: <players> <things> <exits> <rooms> <garbage>
		return new CallState("0 0 0 0 0");
	}

	[SharpFunction(Name = "money", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Money(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// money() returns the money/pennies attribute value
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				// Try to get the MONEY or PENNIES attribute
				var moneyAttr = await AttributeService!.GetAttributeAsync(
					executor, found, "MONEY", IAttributeService.AttributeMode.Read);

				if (moneyAttr.IsAttribute)
				{
					var attr = moneyAttr.AsAttribute.Last();
					var attrValue = attr.Value.ToPlainText();
					if (int.TryParse(attrValue, out var money))
					{
						return new CallState(money);
					}
				}

				// Default to 0 if no money attribute
				return new CallState(0);
			});
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
	public static async ValueTask<CallState> Moniker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// moniker() returns the moniker attribute or the name if no moniker is set
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				// Try to get the MONIKER attribute
				var monikerAttr = await AttributeService!.GetAttributeAsync(
					executor, found, "MONIKER", IAttributeService.AttributeMode.Read);

				if (monikerAttr.IsAttribute)
				{
					var attr = monikerAttr.AsAttribute.Last();
					var attrValue = attr.Value.ToPlainText();
					if (!string.IsNullOrWhiteSpace(attrValue))
					{
						return new CallState(attrValue);
					}
				}

				// Fall back to name
				return new CallState(found.Object().Name);
			});
	}

	[SharpFunction(Name = "nearby", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Nearby(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// nearby() checks if two objects are in the same location or nearby
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj1Arg = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var obj2Arg = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		var maybeObj1 = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, obj1Arg, LocateFlags.All);

		if (maybeObj1.IsError)
		{
			return maybeObj1.AsError;
		}

		var maybeObj2 = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, obj2Arg, LocateFlags.All);

		if (maybeObj2.IsError)
		{
			return maybeObj2.AsError;
		}

		var obj1 = maybeObj1.AsSharpObject;
		var obj2 = maybeObj2.AsSharpObject;

		// Get the room for both objects
		var room1 = await LocateService.Room(obj1);
		var room2 = await LocateService.Room(obj2);

		// They're nearby if they're in the same room
		return new CallState(room1.Object().DBRef == room2.Object().DBRef);
	}

	[SharpFunction(Name = "playermem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> PlayerMem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(0);

	[SharpFunction(Name = "quota", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Quota(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// quota() returns quota information (objects owned / limit)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				// TODO: Implement actual quota checking when database iteration is available
				// For now, return unlimited quota
				await ValueTask.CompletedTask;
				return new CallState("0 999999");
			});
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
	public static async ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// textsearch() searches for text in object attributes
		// Format: textsearch(<class>, <pattern>, [<attribute>])
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		var classArg = args["0"].Message!.ToPlainText();
		var pattern = args["1"].Message!.ToPlainText();
		var attributePattern = args.TryGetValue("2", out var attrArg) 
			? attrArg.Message!.ToPlainText() 
			: "*";

		// Get all objects to search
		var allObjects = Mediator!.CreateStream(new GetAllObjectsQuery());
		var results = new List<string>();

		// Determine class filter
		AnySharpObject? classObj = null;
		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await LocateService!.Locate(parser, executor, executor, classArg, LocateFlags.All);
			if (!maybeClass.IsValid())
			{
				return new CallState("#-1 INVALID CLASS");
			}
			classObj = maybeClass.AsAnyObject;
		}

		// Search through objects
		await foreach (var obj in allObjects)
		{
			// Check ownership if class is specified
			if (classObj != null)
			{
				var owner = await obj.Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef != classObj.Object().DBRef)
				{
					continue;
				}
			}

			// Get attributes and search
			var attributes = obj.Attributes.Value;
			
			await foreach (var attr in attributes)
			{
				// Check if attribute name matches pattern (simple contains for now)
				if (attributePattern != "*" && !attr.Name.Contains(attributePattern, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				// Check if value contains pattern
				var value = attr.Value.ToPlainText();
				if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				{
					results.Add(new DBRef(obj.Key, obj.CreationTime).ToString());
					break; // Found match in this object, move to next
				}
			}
		}

		return new CallState(string.Join(" ", results));
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

	[SharpFunction(Name = "motd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Motd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/connect
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.ConnectMotd ?? string.Empty);
	}

	[SharpFunction(Name = "wizmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static async ValueTask<CallState> WizMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/wizard
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.WizardMotd ?? string.Empty);
	}

	[SharpFunction(Name = "downmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static async ValueTask<CallState> DownMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/down
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.DownMotd ?? string.Empty);
	}

	[SharpFunction(Name = "fullmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static async ValueTask<CallState> FullMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/full
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.FullMotd ?? string.Empty);
	}
}