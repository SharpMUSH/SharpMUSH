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
using ConfigGenerated = SharpMUSH.Configuration.Generated;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{

	[SharpFunction(Name = "accname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public static async ValueTask<CallState> AccName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
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

				return new CallState(found.Object().Name);
			});
	}

	[SharpFunction(Name = "folderstats", MinArgs = 0, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["folder"])]
	public static async ValueTask<CallState> folderstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		AnySharpObject targetPlayer = executor;
		string? folderSpec = null;

		if (args.Count == 0)
		{
			folderSpec = await Implementation.Commands.MailCommand.MessageListHelper.CurrentMailFolder(
				parser, ObjectDataService!, executor);
		}
		else if (args.Count == 1)
		{
			var arg = args["0"].Message!.ToPlainText()!;

			if (int.TryParse(arg, out var folderNum) && folderNum >= 0 && folderNum <= 15)
			{
				folderSpec = arg;
			}
			else if (arg.All(char.IsUpper) || arg.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
			{
				folderSpec = arg;
			}
			else
			{
				if (!await executor.IsWizard())
				{
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}

				var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
					parser, executor, executor, arg, LocateFlags.PlayersPreference);

				if (locateResult.IsError || locateResult.IsNone)
				{
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
			if (!await executor.IsWizard())
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var playerArg = args["0"].Message!.ToPlainText()!;
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			targetPlayer = locateResult.AsPlayer;
			folderSpec = args["1"].Message!.ToPlainText()!;
		}

		var folderMail = Mediator!.CreateStream(new GetMailListQuery(targetPlayer.AsPlayer, folderSpec ?? "INBOX"));
		var mailArray = await folderMail.ToArrayAsync();

		var read = mailArray.Count(m => m.Read);
		var unread = mailArray.Count(m => !m.Read);
		var cleared = mailArray.Count(m => m.Cleared);

		return new CallState($"{read} {unread} {cleared}");
	}


	[SharpFunction(Name = "restarts", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static async ValueTask<CallState> restarts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return uptimeData?.Reboots ?? 0;
	}

	[SharpFunction(Name = "restarttime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static async ValueTask<CallState> RestartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return (uptimeData?.LastRebootTime ?? DateTimeOffset.Now).ToUnixTimeMilliseconds();
	}

	[SharpFunction(Name = "pidinfo", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["pid", "field", "delimiter"])]
	public static async ValueTask<CallState> PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var pidStr = args["0"].Message!.ToPlainText();

		if (!long.TryParse(pidStr, out var pid))
		{
			return new CallState(ErrorMessages.Returns.InvalidPid);
		}

		var field = args.TryGetValue("1", out var fieldArg)
			? fieldArg.Message!.ToPlainText().ToLowerInvariant()
			: null;
		var delimiter = args.TryGetValue("2", out var delimArg)
			? delimArg.Message!.ToPlainText()
			: " ";

		var semaphoreTasks = await Mediator!.CreateStream(new ScheduleSemaphoreQuery(pid)).ToListAsync();
		if (semaphoreTasks.Count > 0)
		{
			var task = semaphoreTasks[0];
			return FormatTaskInfo(task, field, delimiter);
		}

		return new CallState(ErrorMessages.Returns.NoSuchPid);
	}

	private static CallState FormatTaskInfo(SemaphoreTaskData task, string? field, string delimiter)
	{
		if (field == null)
		{
			var parts = new List<string>
			{
				task.Pid.ToString(),
				task.Command.ToPlainText(),
				task.Owner.ToString(),
				"waiting",
				task.RunDelay?.TotalSeconds.ToString("F2") ?? "0"
			};
			return new CallState(string.Join(delimiter, parts));
		}

		return field switch
		{
			"pid" => new CallState(task.Pid.ToString()),
			"command" => new CallState(task.Command.ToPlainText()),
			"executor" => new CallState(task.Owner.ToString()),
			"status" => new CallState("waiting"),
			"delay" => new CallState(task.RunDelay?.TotalSeconds.ToString("F2") ?? "0"),
			"semaphore" => new CallState(task.SemaphoreSource.ToString()),
			_ => new CallState(ErrorMessages.Returns.InvalidField)
		};
	}

	[SharpFunction(Name = "alias", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
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

				if (args.Count == 1)
				{
					return ValueTask.FromResult(new CallState(aliases?.FirstOrDefault() ?? string.Empty));
				}

				var indexArg = args["1"].Message!.ToPlainText();
				if (!int.TryParse(indexArg, out var index) || index < 1)
				{
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidAliasIndex));
				}

				return ValueTask.FromResult(new CallState(
					aliases != null && index <= aliases.Length
						? aliases[index - 1]
						: string.Empty));
			});
	}

	[SharpFunction(Name = "findable", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "looker"])]
	public static async ValueTask<CallState> Findable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
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

		var maybeTarget = await LocateService.Locate(parser, looker, executor, targetArg, LocateFlags.All);

		return new CallState(maybeTarget.IsValid());
	}

	[SharpFunction(Name = "fullalias", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
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

	[SharpFunction(Name = "fullname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
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
						{ IsExit: true, AsExit: var exit } => [name, .. exit.Aliases ?? []],
						_ => [name]
					})));
			});
	}

	[SharpFunction(Name = "getpids", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["type"])]
	public static async ValueTask<CallState> GetProcessIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		var split = HelperFunctions.SplitDbRefAndOptionalAttr(arg0);
		if (split.IsT1)
		{
			return string.Format(ErrorMessages.Returns.BadArgumentFormat, "getpids");
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

	[SharpFunction(Name = "powers", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
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
						return ErrorMessages.Returns.NoSideFx;
					}

					return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
						parser, executor, executor, obj!.Message!.ToPlainText(), LocateFlags.All,
						async found =>
							await ManipulateSharpObjectService!.SetPower(executor, found, power!.Message!.ToPlainText(), true));
				}
		}
	}

	[SharpFunction(Name = "haspower", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "power"])]
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

	[SharpFunction(Name = "hastype", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "type"])]
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
			return new CallState(ErrorMessages.Returns.NoSuchType);
		}

		var located = maybeLocate.AsSharpObject;
		var hasType = typeQuery.Any(validType => located.HasType(validType));

		return new CallState(hasType);
	}

	[SharpFunction(Name = "iname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> IName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found => ValueTask.FromResult(new CallState(MModule.plainText(MModule.single(found.Object().Name)))));
	}

	[SharpFunction(Name = "lpids", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public static async ValueTask<CallState> LPIDs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;
		var target = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, "me");
		var queueTypesStr = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "wait semaphore").ToPlainText()
			.ToUpperInvariant();

		var queueTypes = queueTypesStr.Split(" ", StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

		if (queueTypes.Any(type => type is not "WAIT" and not "SEMAPHORE" and not "INDEPENDENT"))
		{
			return new CallState(ErrorMessages.Returns.InvalidQueueType);
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
		var locationDBRef = located.Object().DBRef;

		bool includeWait = queueTypes.Contains("WAIT");
		bool includeSemaphore = queueTypes.Contains("SEMAPHORE");
		bool independent = queueTypes.Contains("INDEPENDENT");

		if (!includeWait && !includeSemaphore)
		{
			includeWait = true;
			includeSemaphore = true;
		}

		var allPids = new List<long>();

		if (includeWait)
		{
			var waitPids = Mediator!.CreateStream(new ScheduleDelayQuery(locationDBRef));
			await foreach (var pid in waitPids)
			{
				allPids.Add(pid);
			}
		}

		if (includeSemaphore)
		{
			var semaphorePids = Mediator!.CreateStream(new ScheduleSemaphoreQuery(locationDBRef));
			await foreach (var taskData in semaphorePids)
			{
				allPids.Add(taskData.Pid);
			}
		}

		// Note: INDEPENDENT filtering would require owner-based filtering
		// Current implementation returns PIDs for the specific DBRef
		// In PennMUSH, INDEPENDENT filters out tasks from objects with same owner but different DBRef
		// This would require extending the query to check task executor owner vs target owner

		return new CallState(string.Join(' ', allPids.OrderBy(x => x)));
	}

	[SharpFunction(Name = "lstats", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["type"])]
	public static async ValueTask<CallState> LStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var typeFilter = args.TryGetValue("0", out var typeArg)
			? typeArg.Message!.ToPlainText().ToUpperInvariant()
			: null;

		var allObjects = await Mediator!.CreateStream(new GetAllObjectsQuery())
			.ToListAsync();

		if (!string.IsNullOrEmpty(typeFilter))
		{
			var count = typeFilter switch
			{
				"PLAYER" or "PLAYERS" => allObjects.Count(o => o.Type == "PLAYER"),
				"THING" or "THINGS" => allObjects.Count(o => o.Type == "THING"),
				"EXIT" or "EXITS" => allObjects.Count(o => o.Type == "EXIT"),
				"ROOM" or "ROOMS" => allObjects.Count(o => o.Type == "ROOM"),
				"GARBAGE" => 0, // SharpMUSH doesn't track garbage separately
				_ => -1
			};

			return count >= 0 ? new CallState(count.ToString()) : new CallState(ErrorMessages.Returns.InvalidType);
		}

		var players = allObjects.Count(o => o.Type == "PLAYER");
		var things = allObjects.Count(o => o.Type == "THING");
		var exits = allObjects.Count(o => o.Type == "EXIT");
		var rooms = allObjects.Count(o => o.Type == "ROOM");
		var garbage = 0; // SharpMUSH doesn't track garbage separately

		return new CallState($"{players} {things} {exits} {rooms} {garbage}");
	}

	[SharpFunction(Name = "money", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Money(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MoneyFunctionNotSupported), executor);
		return new CallState(ErrorMessages.Returns.ErrorNotSupported);
	}

	[SharpFunction(Name = "mudname", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> MudName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Configuration!.CurrentValue.Net.MudName);

	[SharpFunction(Name = "mudurl", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> MudURL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Configuration!.CurrentValue.Net.MudUrl ?? "");

	/// <summary>
	/// locale() — returns the BCP-47 locale tag that is active on the executor's current
	/// connection (e.g. "en", "fr"). Returns "en" when no locale has been set.
	/// </summary>
	[SharpFunction(Name = "locale", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> LocaleFunc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var handle = parser.CurrentState.Handle;
		if (handle is null)
			return ValueTask.FromResult(new CallState("en"));

		var conn = ConnectionService!.Get(handle.Value);
		if (conn is null)
			return ValueTask.FromResult(new CallState("en"));

		conn.Metadata.TryGetValue("Locale", out var locale);
		return ValueTask.FromResult(new CallState(string.IsNullOrEmpty(locale) ? "en" : locale));
	}

	[SharpFunction(Name = "name", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "new name"])]
	public static async ValueTask<CallState> Name(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
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

		await NotifyService!.Notify(executor, ErrorMessages.Returns.NoSideFx);
		return false;
	}

	[SharpFunction(Name = "moniker", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Moniker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
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

				return new CallState(found.Object().Name);
			});
	}

	[SharpFunction(Name = "nearby", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object1", "object2"])]
	public static async ValueTask<CallState> Nearby(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
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

		var room1 = await LocateService.Room(obj1);
		var room2 = await LocateService.Room(obj2);

		return new CallState(room1.Object().DBRef == room2.Object().DBRef);
	}

	[SharpFunction(Name = "playermem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public static ValueTask<CallState> PlayerMem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(0);

	[SharpFunction(Name = "quota", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Quota(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var owner = await found.Object().Owner.WithCancellation(CancellationToken.None);

				if (owner is null)
				{
					return new CallState("0 0");
				}

				var ownedCount = await Mediator!.Send(new GetOwnedObjectCountQuery(owner));

				return new CallState($"{ownedCount} {owner.Quota}");
			});
	}

	[SharpFunction(Name = "type", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Type(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All, found => found.TypeString());
	}

	[SharpFunction(Name = "textsearch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		var classArg = args["0"].Message!.ToPlainText();
		var pattern = args["1"].Message!.ToPlainText();
		var attributePattern = args.TryGetValue("2", out var attrArg)
			? attrArg.Message!.ToPlainText()
			: "*";

		var allObjects = Mediator!.CreateStream(new GetAllObjectsQuery());
		var results = new List<string>();

		AnySharpObject? classObj = null;
		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await LocateService!.Locate(parser, executor, executor, classArg, LocateFlags.All);
			if (!maybeClass.IsValid())
			{
				return new CallState(ErrorMessages.Returns.InvalidClass);
			}
			classObj = maybeClass.AsAnyObject;
		}

		await foreach (var obj in allObjects)
		{
			if (classObj != null)
			{
				var owner = await obj.Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef != classObj.Object().DBRef)
				{
					continue;
				}
			}

			var attributes = obj.Attributes.Value;

			await foreach (var attr in attributes)
			{
				if (attributePattern != "*" && !attr.Name.Contains(attributePattern, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var value = attr.Value.ToPlainText();
				if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				{
					results.Add(new DBRef(obj.Key, obj.CreationTime).ToString());
					break;
				}
			}
		}

		return new CallState(string.Join(" ", results));
	}

	[SharpFunction(Name = "colors", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["ansi-string", "strip"])]
	public static ValueTask<CallState> Colors(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var colorsConfig = ColorConfiguration?.CurrentValue;

		if (colorsConfig == null || colorsConfig.Colors.Length == 0)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		if (args.Count == 0 || string.IsNullOrEmpty(args["0"]?.Message?.ToPlainText()))
		{
			var allColors = colorsConfig.Colors
				.Select(c => c.name)
				.Distinct()
				.ToList();

			return ValueTask.FromResult(new CallState(string.Join(" ", allColors)));
		}

		if (args.Count == 1 || (args.Count == 2 && string.IsNullOrEmpty(args["1"]?.Message?.ToPlainText())))
		{
			var wildcardPattern = args["0"].Message!.ToString();
			var matchingColors = colorsConfig.Colors
				.Where(c => MModule.isWildcardMatch2(MModule.single(c.name), wildcardPattern))
				.Select(c => c.name)
				.Distinct()
				.ToList();

			return ValueTask.FromResult(new CallState(string.Join(" ", matchingColors)));
		}

		var colorSpec = args["0"].Message!.ToString();
		var formatSpec = args["1"].Message!.ToString().ToLowerInvariant();

		var includeStyles = formatSpec.Contains("styles");
		var formatType = formatSpec.Replace("styles", "").Trim();

		var (foregroundSpec, backgroundSpec, styles) = ParseColorSpecification(colorSpec);

		var result = formatType switch
		{
			"hex" or "x" => FormatColorsAsHex(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"rgb" or "r" => FormatColorsAsRgb(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"xterm256" or "d" => FormatColorsAsXterm(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig, hexFormat: false),
			"xterm256x" or "h" => FormatColorsAsXterm(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig, hexFormat: true),
			"16color" or "c" => FormatColorsAs16Color(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"name" => FormatColorsAsName(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"auto" => FormatColorsAsAuto(colorSpec, foregroundSpec, backgroundSpec, styles, includeStyles),
			_ => ErrorMessages.Returns.InvalidFormat
		};

		return ValueTask.FromResult(new CallState(result));
	}

	/// <summary>
	/// Parse a color specification into foreground, background, and styles
	/// </summary>
	private static (string? foreground, string? background, string styles) ParseColorSpecification(string spec)
	{
		string? foreground = null;
		string? background = null;
		var stylesBuilder = new System.Text.StringBuilder();

		var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		foreach (var part in parts)
		{
			if (part.StartsWith('/'))
			{
				background = part[1..];
				continue;
			}

			var i = 0;
			var currentStyles = stylesBuilder.ToString();
			while (i < part.Length && IsAnsiControlChar(part[i]) && part[i] != '+' && part[i] != '#')
			{
				var currentChar = part[i];
				if (!currentStyles.Contains(currentChar))
				{
					stylesBuilder.Append(currentChar);
					currentStyles = stylesBuilder.ToString();
				}
				i++;
			}

			var colorPart = part[i..];
			if (!string.IsNullOrWhiteSpace(colorPart))
			{
				foreground = colorPart;
			}
		}

		return (foreground, background, stylesBuilder.ToString());
	}

	private static bool IsAnsiControlChar(char ch)
	{
		return ch is 'f' or 'u' or 'i' or 'h' or
					 'x' or 'r' or 'g' or 'y' or 'b' or 'm' or 'c' or 'w' or
					 'X' or 'R' or 'G' or 'Y' or 'B' or 'M' or 'C' or 'W';
	}

	private static string FormatColorsAsHex(string? foreground, string? background, string styles, bool includeStyles,
		SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var result = new List<string>();

		if (includeStyles && !string.IsNullOrEmpty(styles))
		{
			result.Add(styles);
		}

		if (!string.IsNullOrEmpty(foreground))
		{
			var hex = ConvertColorToHex(foreground, config);
			if (hex != null)
			{
				result.Add(hex);
			}
		}

		if (!string.IsNullOrEmpty(background))
		{
			var hex = ConvertColorToHex(background, config);
			if (hex != null)
			{
				result.Add($"/{hex}");
			}
		}

		return result.Count > 0 ? string.Join(" ", result) : ErrorMessages.Returns.InvalidColor;
	}

	private static string FormatColorsAsRgb(string? foreground, string? background, string styles, bool includeStyles,
		SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var result = new List<string>();

		if (includeStyles && !string.IsNullOrEmpty(styles))
		{
			result.Add(styles);
		}

		if (!string.IsNullOrEmpty(foreground))
		{
			var rgb = ConvertColorToRgb(foreground, config);
			if (rgb != null)
			{
				result.Add(rgb);
			}
		}

		if (!string.IsNullOrEmpty(background))
		{
			var rgb = ConvertColorToRgb(background, config);
			if (rgb != null)
			{
				result.Add($"/{rgb}");
			}
		}

		return result.Count > 0 ? string.Join(" ", result) : ErrorMessages.Returns.InvalidColor;
	}

	private static string FormatColorsAsXterm(string? foreground, string? background, string styles, bool includeStyles,
		SharpMUSH.Configuration.Options.ColorsOptions config, bool hexFormat)
	{
		var result = new List<string>();

		if (includeStyles && !string.IsNullOrEmpty(styles))
		{
			result.Add(styles);
		}

		if (!string.IsNullOrEmpty(foreground))
		{
			var xterm = ConvertColorToXterm(foreground, config);
			if (xterm != null)
			{
				result.Add(hexFormat ? xterm.Value.ToString("x") : xterm.Value.ToString());
			}
		}

		if (!string.IsNullOrEmpty(background))
		{
			var xterm = ConvertColorToXterm(background, config);
			if (xterm != null)
			{
				var formatted = hexFormat ? xterm.Value.ToString("x") : xterm.Value.ToString();
				result.Add($"/{formatted}");
			}
		}

		return result.Count > 0 ? string.Join(" ", result) : ErrorMessages.Returns.InvalidColor;
	}

	private static string FormatColorsAs16Color(string? foreground, string? background, string styles, bool includeStyles,
		SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var result = new List<string>();

		if (includeStyles && !string.IsNullOrEmpty(styles))
		{
			result.Add(styles);
		}

		if (!string.IsNullOrEmpty(foreground))
		{
			var ansi = ConvertColorTo16Color(foreground, config);
			if (ansi != null)
			{
				result.Add(ansi);
			}
		}

		if (!string.IsNullOrEmpty(background))
		{
			var ansi = ConvertColorTo16Color(background, config);
			if (ansi != null)
			{
				result.Add(ansi.ToUpperInvariant());
			}
		}

		return result.Count > 0 ? string.Join(" ", result) : ErrorMessages.Returns.InvalidColor;
	}

	private static string FormatColorsAsName(string? foreground, string? background, string styles, bool includeStyles,
		SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var result = new List<string>();

		if (includeStyles && !string.IsNullOrEmpty(styles))
		{
			result.Add(styles);
		}

		if (!string.IsNullOrEmpty(foreground))
		{
			var names = ConvertColorToNames(foreground, config);
			if (names.Count > 0)
			{
				result.AddRange(names);
			}
			else
			{
				return ErrorMessages.Returns.NoMatchingColorName;
			}
		}

		if (!string.IsNullOrEmpty(background))
		{
			var names = ConvertColorToNames(background, config);
			if (names.Count > 0)
			{
				result.AddRange(names.Select(n => $"/{n}"));
			}
			else
			{
				return ErrorMessages.Returns.NoMatchingColorName;
			}
		}

		return result.Count > 0 ? string.Join(" ", result) : ErrorMessages.Returns.NoMatchingColorName;
	}

	private static string FormatColorsAsAuto(string originalSpec, string? foreground, string? background, string styles, bool includeStyles)
	{
		return originalSpec;
	}

	private static string? ConvertColorToHex(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		if (colorSpec.StartsWith('#'))
		{
			return colorSpec;
		}

		if (colorSpec.StartsWith('+'))
		{
			colorSpec = colorSpec[1..];
		}

		if (config.ColorsByName.TryGetValue(colorSpec, out var color))
		{
			return "#" + color.rgb[2..];
		}

		if (int.TryParse(colorSpec, out var xtermNum))
		{
			var xtermColors = config.Colors.Where(c => c.xterm == xtermNum).ToList();
			if (xtermColors.Count > 0)
			{
				return "#" + xtermColors[0].rgb[2..];
			}
		}

		if (colorSpec.StartsWith("xterm") && int.TryParse(colorSpec[5..], out var xtermNum2))
		{
			var xtermColors = config.Colors.Where(c => c.xterm == xtermNum2).ToList();
			if (xtermColors.Count > 0)
			{
				return "#" + xtermColors[0].rgb[2..];
			}
		}

		return null;
	}

	private static string? ConvertColorToRgb(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var hex = ConvertColorToHex(colorSpec, config);
		if (hex == null)
		{
			return null;
		}

		// Parse hex to RGB using Span<char> to avoid substring allocations
		var hexSpan = hex.AsSpan();
		var r = int.Parse(hexSpan.Slice(1, 2), System.Globalization.NumberStyles.HexNumber);
		var g = int.Parse(hexSpan.Slice(3, 2), System.Globalization.NumberStyles.HexNumber);
		var b = int.Parse(hexSpan.Slice(5, 2), System.Globalization.NumberStyles.HexNumber);

		return $"{r} {g} {b}";
	}

	private static int? ConvertColorToXterm(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		if (int.TryParse(colorSpec, out var xtermNum) && xtermNum >= 0 && xtermNum <= 255)
		{
			return xtermNum;
		}

		if (colorSpec.StartsWith("xterm") && int.TryParse(colorSpec[5..], out var xtermNum2))
		{
			return xtermNum2;
		}

		if (colorSpec.StartsWith('+'))
		{
			colorSpec = colorSpec[1..];
		}

		if (config.ColorsByName.TryGetValue(colorSpec, out var color))
		{
			return color.xterm;
		}

		return null;
	}

	private static string? ConvertColorTo16Color(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var xterm = ConvertColorToXterm(colorSpec, config);
		if (xterm == null)
		{
			return null;
		}

		// This is a simplified mapping - PennMUSH has more sophisticated color distance calculations
		return xterm.Value switch
		{
			0 => "x",
			1 => "r",
			2 => "g",
			3 => "y",
			4 => "b",
			5 => "m",
			6 => "c",
			7 => "w",
			8 => "hx",
			9 => "hr",
			10 => "hg",
			11 => "hy",
			12 => "hb",
			13 => "hm",
			14 => "hc",
			15 => "hw",
			_ => MapXtermColorTo16Color(xterm.Value, config)
		};
	}

	private static string MapXtermColorTo16Color(int xterm, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var colorMatch = config.Colors.Where(c => c.xterm == xterm).ToList();
		if (colorMatch.Count == 0 || colorMatch[0].rgb == null)
		{
			return "w";
		}

		var rgb = colorMatch[0].rgb;
		var rgbSpan = rgb.AsSpan();
		var r = int.Parse(rgbSpan.Slice(2, 2), System.Globalization.NumberStyles.HexNumber);
		var g = int.Parse(rgbSpan.Slice(4, 2), System.Globalization.NumberStyles.HexNumber);
		var b = int.Parse(rgbSpan.Slice(6, 2), System.Globalization.NumberStyles.HexNumber);

		var brightness = (r + g + b) / 3;
		var highlight = brightness > 128 ? "h" : "";

		if (r > g && r > b)
		{
			return highlight + "r";
		}
		else if (g > r && g > b)
		{
			return highlight + "g";
		}
		else if (b > r && b > g)
		{
			return highlight + "b";
		}
		else if (r > b && g > b)
		{
			return highlight + "y";
		}
		else if (r > g && b > g)
		{
			return highlight + "m";
		}
		else if (g > r && b > r)
		{
			return highlight + "c";
		}
		else if (brightness < 64)
		{
			return "x";
		}
		else
		{
			return highlight + "w";
		}
	}

	private static List<string> ConvertColorToNames(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var result = new List<string>();

		if (colorSpec.StartsWith('+'))
		{
			colorSpec = colorSpec[1..];
		}

		if (config.ColorsByName.TryGetValue(colorSpec, out var color))
		{
			if (config.ColorsByRgb.TryGetValue(color.rgb, out var colors))
			{
				result.AddRange(colors.Select(c => c.name));
			}
			return result;
		}

		if (colorSpec.StartsWith('#'))
		{
			var rgb = "0x" + colorSpec[1..];
			if (config.ColorsByRgb.TryGetValue(rgb, out var colors))
			{
				result.AddRange(colors.Select(c => c.name));
			}
			return result;
		}

		if (int.TryParse(colorSpec, out var xtermNum) ||
			(colorSpec.StartsWith("xterm") && int.TryParse(colorSpec[5..], out xtermNum)))
		{
			var xtermColors = config.Colors.Where(c => c.xterm == xtermNum).ToList();
			if (xtermColors.Count > 0)
			{
				var rgb = xtermColors[0].rgb;
				if (config.ColorsByRgb.TryGetValue(rgb, out var colors))
				{
					result.AddRange(colors.Select(c => c.name));
				}
			}
		}

		return result;
	}

	[SharpFunction(Name = "motd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static async ValueTask<CallState> Motd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.ConnectMotd ?? string.Empty);
	}

	[SharpFunction(Name = "wizmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public static async ValueTask<CallState> WizMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.WizardMotd ?? string.Empty);
	}

	[SharpFunction(Name = "downmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public static async ValueTask<CallState> DownMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.DownMotd ?? string.Empty);
	}

	[SharpFunction(Name = "fullmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public static async ValueTask<CallState> FullMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.FullMotd ?? string.Empty);
	}

	[SharpFunction(Name = "CONFIG", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["option"])]
	public static ValueTask<CallState> Config(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		var allOptionNames = ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Keys;

		if (!args.TryGetValue("0", out var optionArg) || string.IsNullOrWhiteSpace(optionArg.Message?.ToPlainText()))
		{
			var optionNames = allOptionNames
				.Select(prop => ConfigGenerated.ConfigMetadata.PropertyToAttributeName[prop].ToLowerInvariant())
				.OrderBy(n => n);
			return ValueTask.FromResult<CallState>(string.Join(" ", optionNames));
		}

		var searchTerm = optionArg.Message!.ToPlainText();

		var matchingProperty = ConfigGenerated.ConfigMetadata.PropertyToAttributeName
			.FirstOrDefault(kvp => kvp.Value.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingProperty.Key != null)
		{
			var value = ConfigGenerated.ConfigAccessor.GetValue(Configuration!.CurrentValue, matchingProperty.Key);
			return ValueTask.FromResult<CallState>(value?.ToString() ?? "");
		}

		return ValueTask.FromResult<CallState>(ErrorMessages.Returns.NoSuchOption);
	}
}