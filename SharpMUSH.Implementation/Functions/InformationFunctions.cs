using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
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
	// Error message constants for colors() function
	private const string ErrorInvalidColor = "#-1 INVALID COLOR";
	private const string ErrorNoMatchingColorName = "#-1 NO MATCHING COLOR NAME";
	private const string ErrorInvalidFormat = "#-1 INVALID FORMAT";

	[SharpFunction(Name = "accname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public async ValueTask<CallState> AccName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var accnameAttr = await _attributeService!.GetAttributeAsync(
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
	public async ValueTask<CallState> folderstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		
		AnySharpObject targetPlayer = executor;
		string? folderSpec = null;

		if (args.Count == 0)
		{
			folderSpec = await Implementation.Commands.MailCommand.MessageListHelper.CurrentMailFolder(
				parser, _objectDataService!, executor);
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
				var locateResult = await _locateService!.LocateAndNotifyIfInvalid(
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
						parser, _objectDataService!, targetPlayer);
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
			var locateResult = await _locateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);
			
			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState("#-1 NO SUCH PLAYER");
			}

			targetPlayer = locateResult.AsPlayer;
			folderSpec = args["1"].Message!.ToPlainText()!;
		}

		// Get mail for the folder
		var folderMail = _mediator!.CreateStream(new GetMailListQuery(targetPlayer.AsPlayer, folderSpec ?? "INBOX"));
		var mailArray = await folderMail.ToArrayAsync();
		
		var read = mailArray.Count(m => m.Read);
		var unread = mailArray.Count(m => !m.Read);
		var cleared = mailArray.Count(m => m.Cleared);

		return new CallState($"{read} {unread} {cleared}");
	}


	[SharpFunction(Name = "restarts", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> restarts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await _objectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return uptimeData?.Reboots ?? 0;
	}

	[SharpFunction(Name = "restarttime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> RestartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await _objectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return (uptimeData?.LastRebootTime ?? DateTimeOffset.Now).ToUnixTimeMilliseconds();
	}

	[SharpFunction(Name = "pidinfo", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["pid", "field", "delimiter"])]
	public async ValueTask<CallState> PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// pidinfo() returns information about a process ID
		// Format: pidinfo(<pid>[, <field>][, <delimiter>])
		var args = parser.CurrentState.Arguments;
		var pidStr = args["0"].Message!.ToPlainText();
		
		if (!long.TryParse(pidStr, out var pid))
		{
			return new CallState("#-1 INVALID PID");
		}

		var field = args.TryGetValue("1", out var fieldArg) 
			? fieldArg.Message!.ToPlainText().ToLowerInvariant()
			: null;
		var delimiter = args.TryGetValue("2", out var delimArg)
			? delimArg.Message!.ToPlainText()
			: " ";

		// Query semaphore tasks via _mediator
		var semaphoreTasks = await _mediator!.CreateStream(new ScheduleSemaphoreQuery(pid)).ToListAsync();
		if (semaphoreTasks.Count > 0)
		{
			var task = semaphoreTasks[0];
			return FormatTaskInfo(task, field, delimiter);
		}

		// If not found in semaphore tasks, return "NO SUCH PID"
		return new CallState("#-1 NO SUCH PID");
	}

	private static CallState FormatTaskInfo(SemaphoreTaskData task, string? field, string delimiter)
	{
		if (field == null)
		{
			// Return all fields: pid command executor status delay
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

		// Return specific field
		return field switch
		{
			"pid" => new CallState(task.Pid.ToString()),
			"command" => new CallState(task.Command.ToPlainText()),
			"executor" => new CallState(task.Owner.ToString()),
			"status" => new CallState("waiting"),
			"delay" => new CallState(task.RunDelay?.TotalSeconds.ToString("F2") ?? "0"),
			"semaphore" => new CallState(task.SemaphoreSource.ToString()),
			_ => new CallState("#-1 INVALID FIELD")
		};
	}

	[SharpFunction(Name = "alias", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public async ValueTask<CallState> Alias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var args = parser.CurrentState.Arguments;

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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

	[SharpFunction(Name = "findable", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "looker"])]
	public async ValueTask<CallState> Findable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// findable() checks if the first object can find the second object
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var lookerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var targetArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		var maybeLooker = await _locateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, lookerArg, LocateFlags.All);

		if (maybeLooker.IsError)
		{
			return maybeLooker.AsError;
		}

		var looker = maybeLooker.AsSharpObject;

		// Try to locate the target from the looker's perspective
		var maybeTarget = await _locateService.Locate(parser, looker, executor, targetArg, LocateFlags.All);

		// If we can locate it, it's findable
		return new CallState(maybeTarget.IsValid());
	}

	[SharpFunction(Name = "fullalias", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> FullAlias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
	public async ValueTask<CallState> FullName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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

	[SharpFunction(Name = "getpids", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["type"])]
	public async ValueTask<CallState> GetProcessIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		var split = HelperFunctions.SplitDbRefAndOptionalAttr(arg0);
		if (split.IsT1)
		{
			return string.Format(Errors.ErrorBadArgumentFormat, "getpids");
		}

		var (db, attr) = split.AsT0;

		if (attr is null)
		{
			return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, db, LocateFlags.All,
				async found =>
				{
					var queryResult = _mediator!.CreateStream(new ScheduleSemaphoreQuery(found.Object().DBRef));
					var pids = queryResult.Select(x => MModule.single(x.Pid.ToString()));
					return MModule.multipleWithDelimiter(MModule.single(" "), await pids.ToArrayAsync());
				});
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, db, LocateFlags.All,
			async found =>
			{
				var dbAttr = new DbRefAttribute(found.Object().DBRef, attr.Split("`"));
				var queryResult = _mediator!.CreateStream(new ScheduleSemaphoreQuery(dbAttr));
				var pids = queryResult.Select(x => MModule.single(x.Pid.ToString()));
				return MModule.multipleWithDelimiter(MModule.single(" "), await pids.ToArrayAsync());
			});
	}

	[SharpFunction(Name = "powers", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Powers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var obj);
		parser.CurrentState.Arguments.TryGetValue("1", out var power);
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		switch (parser.CurrentState.Arguments.Count)
		{
			case 0:
			{
				var allPowers = (_mediator!.CreateStream(new GetPowersQuery()))
					.Select(x => MModule.single(x.Name));
				return MModule.multipleWithDelimiter(MModule.single(" "), await allPowers.ToArrayAsync());
			}

			case 1:
				return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, obj!.Message!.ToPlainText(), LocateFlags.All,
					async found => MModule.multipleWithDelimiter(MModule.single(" "),
						await found.Object()
							.Powers.Value
							.Select(x => MModule.single(x.Name)).ToArrayAsync()));

			default:
			{
				if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
				{
					return Errors.ErrorNoSideFx;
				}

				return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, obj!.Message!.ToPlainText(), LocateFlags.All,
					async found =>
						await _manipulateSharpObjectService!.SetPower(executor, found, power!.Message!.ToPlainText(), true));
			}
		}
	}

	[SharpFunction(Name = "haspower", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "power"])]
	public async ValueTask<CallState> HasPower(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var toLocate = parser.CurrentState.Arguments["0"].Message!;
		var power = MModule.plainText(parser.CurrentState.Arguments["1"].Message).ToUpper();
		var maybeLocate = await
			_locateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, toLocate.ToPlainText(),
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
	public async ValueTask<CallState> HasType(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var toLocate = parser.CurrentState.Arguments["0"].Message!;
		var typeQuery = MModule.plainText(parser.CurrentState.Arguments["1"].Message).ToUpper().Split(" ");
		var maybeLocate = await
			_locateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, toLocate.ToPlainText(),
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

	[SharpFunction(Name = "iname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> IName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// iname() returns the initial/internal name (name without ANSI codes)
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found => ValueTask.FromResult(new CallState(MModule.plainText(MModule.single(found.Object().Name)))));
	}

	[SharpFunction(Name = "lpids", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public async ValueTask<CallState> LPIDs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;
		var target = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, "me");
		var queueTypesStr = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "wait semaphore").ToPlainText()
			.ToUpperInvariant();
		
		var queueTypes = queueTypesStr.Split(" ", StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

		if (queueTypes.Any(type => type is not "WAIT" and not "SEMAPHORE" and not "INDEPENDENT"))
		{
			return new CallState("#-1 INVALID QUEUE TYPE.");
		}

		var maybeLocate = await
			_locateService!.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, target.ToPlainText(),
				LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var located = maybeLocate.AsSharpObject;
		var locationDBRef = located.Object().DBRef;

		// Determine which queues to query
		bool includeWait = queueTypes.Contains("WAIT");
		bool includeSemaphore = queueTypes.Contains("SEMAPHORE");
		bool independent = queueTypes.Contains("INDEPENDENT");

		// If no specific queue type is specified (only INDEPENDENT), default to wait+semaphore
		if (!includeWait && !includeSemaphore)
		{
			includeWait = true;
			includeSemaphore = true;
		}

		var allPids = new List<long>();

		// Get Wait queue PIDs (from @wait delays)
		if (includeWait)
		{
			var waitPids = _mediator!.CreateStream(new ScheduleDelayQuery(locationDBRef));
			await foreach (var pid in waitPids)
			{
				allPids.Add(pid);
			}
		}

		// Get Semaphore queue PIDs
		if (includeSemaphore)
		{
			var semaphorePids = _mediator!.CreateStream(new ScheduleSemaphoreQuery(locationDBRef));
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
	public async ValueTask<CallState> LStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lstats() returns statistics about objects in the database
		// Format: <players> <things> <exits> <rooms> <garbage>
		var args = parser.CurrentState.Arguments;
		var typeFilter = args.TryGetValue("0", out var typeArg) 
			? typeArg.Message!.ToPlainText().ToUpperInvariant() 
			: null;

		// Get all objects from the database
		var allObjects = await _mediator!.CreateStream(new GetAllObjectsQuery())
			.ToListAsync();

		// If a specific type is requested, filter and count only that type
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

			return count >= 0 ? new CallState(count.ToString()) : new CallState("#-1 INVALID TYPE");
		}

		// Count each type
		var players = allObjects.Count(o => o.Type == "PLAYER");
		var things = allObjects.Count(o => o.Type == "THING");
		var exits = allObjects.Count(o => o.Type == "EXIT");
		var rooms = allObjects.Count(o => o.Type == "ROOM");
		var garbage = 0; // SharpMUSH doesn't track garbage separately

		return new CallState($"{players} {things} {exits} {rooms} {garbage}");
	}

	[SharpFunction(Name = "money", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Money(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Money/pennies are not supported in SharpMUSH
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		await _notifyService!.Notify(executor, "The money() function is not supported. SharpMUSH does not track money or pennies.");
		return new CallState("#-1 NOT SUPPORTED");
	}

	[SharpFunction(Name = "mudname", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> MudName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(_configuration!.CurrentValue.Net.MudName);

	[SharpFunction(Name = "mudurl", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> MudURL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(_configuration!.CurrentValue.Net.MudUrl ?? "");

	[SharpFunction(Name = "name", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "new name"])]
	public async ValueTask<CallState> Name(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		//   name(<object>[, <new name>])
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var newName = parser.CurrentState.Arguments.GetValueOrDefault("1");

		if (newName is null)
		{
			return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, obj, LocateFlags.All,
				found => found.Object().Name);
		}

		if (_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, obj, LocateFlags.All,
				async found =>
					await _manipulateSharpObjectService!.SetName(executor, found, newName.Message!, true));
		}

		await _notifyService!.Notify(executor, Errors.ErrorNoSideFx);
		return false;
	}

	[SharpFunction(Name = "moniker", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Moniker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// moniker() returns the moniker attribute or the name if no moniker is set
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				// Try to get the MONIKER attribute
				var monikerAttr = await _attributeService!.GetAttributeAsync(
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

	[SharpFunction(Name = "nearby", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object1", "object2"])]
	public async ValueTask<CallState> Nearby(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// nearby() checks if two objects are in the same location or nearby
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj1Arg = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var obj2Arg = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		var maybeObj1 = await _locateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, obj1Arg, LocateFlags.All);

		if (maybeObj1.IsError)
		{
			return maybeObj1.AsError;
		}

		var maybeObj2 = await _locateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, obj2Arg, LocateFlags.All);

		if (maybeObj2.IsError)
		{
			return maybeObj2.AsError;
		}

		var obj1 = maybeObj1.AsSharpObject;
		var obj2 = maybeObj2.AsSharpObject;

		// Get the room for both objects
		var room1 = await _locateService.Room(obj1);
		var room2 = await _locateService.Room(obj2);

		// They're nearby if they're in the same room
		return new CallState(room1.Object().DBRef == room2.Object().DBRef);
	}

	[SharpFunction(Name = "playermem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public ValueTask<CallState> PlayerMem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(0);

	[SharpFunction(Name = "quota", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Quota(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// quota() returns quota information (objects owned / limit)
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				// Get the player owner of the object
				var owner = await found.Object().Owner.WithCancellation(CancellationToken.None);
				
				// Object has no owner - return "0 0" (0 objects owned, 0 quota limit)
				if (owner is null)
				{
					return new CallState("0 0");
				}
				
				// Get the actual count of objects owned by the player
				var ownedCount = await _mediator!.Send(new GetOwnedObjectCountQuery(owner));
				
				// Return "owned quota" format (e.g., "42 100" means 42 objects owned of 100 quota)
				return new CallState($"{ownedCount} {owner.Quota}");
			});
	}

	[SharpFunction(Name = "type", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Type(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All, found => found.TypeString());
	}

	[SharpFunction(Name = "textsearch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public async ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// textsearch() searches for text in object attributes
		// Format: textsearch(<class>, <pattern>, [<attribute>])
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var args = parser.CurrentState.Arguments;

		var classArg = args["0"].Message!.ToPlainText();
		var pattern = args["1"].Message!.ToPlainText();
		var attributePattern = args.TryGetValue("2", out var attrArg) 
			? attrArg.Message!.ToPlainText() 
			: "*";

		// Get all objects to search
		var allObjects = _mediator!.CreateStream(new GetAllObjectsQuery());
		var results = new List<string>();

		// Determine class filter
		AnySharpObject? classObj = null;
		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await _locateService!.Locate(parser, executor, executor, classArg, LocateFlags.All);
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

	[SharpFunction(Name = "colors", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["ansi-string", "strip"])]
	public ValueTask<CallState> Colors(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var colorsConfig = _colorConfiguration?.CurrentValue;
		
		if (colorsConfig == null || colorsConfig.Colors.Length == 0)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		// Mode 1: No arguments - return all color names (check if args is empty or first arg is empty)
		if (args.Count == 0 || string.IsNullOrEmpty(args["0"]?.Message?.ToPlainText()))
		{
			var allColors = colorsConfig.Colors
				.Select(c => c.name)
				.Distinct()
				.ToList();
			
			return ValueTask.FromResult(new CallState(string.Join(" ", allColors)));
		}

		// Mode 2: One argument - wildcard filter
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

		// Mode 3: Two arguments - color specification and format
		var colorSpec = args["0"].Message!.ToString();
		var formatSpec = args["1"].Message!.ToString().ToLowerInvariant();

		// Parse the format specification
		var includeStyles = formatSpec.Contains("styles");
		var formatType = formatSpec.Replace("styles", "").Trim();

		// Parse the color specification
		var (foregroundSpec, backgroundSpec, styles) = ParseColorSpecification(colorSpec);

		// Process based on format type
		var result = formatType switch
		{
			"hex" or "x" => FormatColorsAsHex(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"rgb" or "r" => FormatColorsAsRgb(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"xterm256" or "d" => FormatColorsAsXterm(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig, hexFormat: false),
			"xterm256x" or "h" => FormatColorsAsXterm(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig, hexFormat: true),
			"16color" or "c" => FormatColorsAs16Color(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"name" => FormatColorsAsName(foregroundSpec, backgroundSpec, styles, includeStyles, colorsConfig),
			"auto" => FormatColorsAsAuto(colorSpec, foregroundSpec, backgroundSpec, styles, includeStyles),
			_ => ErrorInvalidFormat
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
			// Check if it's a background color (starts with /)
			if (part.StartsWith('/'))
			{
				background = part[1..];
				continue;
			}

			// Extract leading ANSI codes from the part
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

			// The remainder is the color specification
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

		return result.Count > 0 ? string.Join(" ", result) : ErrorInvalidColor;
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

		return result.Count > 0 ? string.Join(" ", result) : ErrorInvalidColor;
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

		return result.Count > 0 ? string.Join(" ", result) : ErrorInvalidColor;
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

		return result.Count > 0 ? string.Join(" ", result) : ErrorInvalidColor;
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
				return ErrorNoMatchingColorName;
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
				return ErrorNoMatchingColorName;
			}
		}

		return result.Count > 0 ? string.Join(" ", result) : ErrorNoMatchingColorName;
	}

	private static string FormatColorsAsAuto(string originalSpec, string? foreground, string? background, string styles, bool includeStyles)
	{
		// For auto mode, return in the same format as provided
		return originalSpec;
	}

	private static string? ConvertColorToHex(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		// If already hex format
		if (colorSpec.StartsWith('#'))
		{
			return colorSpec;
		}

		// If color name (with or without +)
		if (colorSpec.StartsWith('+'))
		{
			colorSpec = colorSpec[1..];
		}

		if (config.ColorsByName.TryGetValue(colorSpec, out var color))
		{
			return "#" + color.rgb[2..]; // Remove "0x" prefix
		}

		// Try xterm color
		if (int.TryParse(colorSpec, out var xtermNum))
		{
			var xtermColors = config.Colors.Where(c => c.xterm == xtermNum).ToList();
			if (xtermColors.Count > 0)
			{
				return "#" + xtermColors[0].rgb[2..];
			}
		}

		// Try xterm prefix
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

		// Parse hex to RGB
		var r = Convert.ToInt32(hex.Substring(1, 2), 16);
		var g = Convert.ToInt32(hex.Substring(3, 2), 16);
		var b = Convert.ToInt32(hex.Substring(5, 2), 16);

		return $"{r} {g} {b}";
	}

	private static int? ConvertColorToXterm(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		// If already xterm format
		if (int.TryParse(colorSpec, out var xtermNum) && xtermNum >= 0 && xtermNum <= 255)
		{
			return xtermNum;
		}

		if (colorSpec.StartsWith("xterm") && int.TryParse(colorSpec[5..], out var xtermNum2))
		{
			return xtermNum2;
		}

		// If color name
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
		// Map to 16-color ANSI codes
		// Basic ANSI colors: x=black, r=red, g=green, y=yellow, b=blue, m=magenta, c=cyan, w=white
		// Add 'h' prefix for highlight (bright) versions
		
		var xterm = ConvertColorToXterm(colorSpec, config);
		if (xterm == null)
		{
			return null;
		}

		// Map xterm256 to 16-color ANSI
		// This is a simplified mapping - PennMUSH has more sophisticated color distance calculations
		return xterm.Value switch
		{
			0 => "x",     // black
			1 => "r",     // red
			2 => "g",     // green
			3 => "y",     // yellow
			4 => "b",     // blue
			5 => "m",     // magenta
			6 => "c",     // cyan
			7 => "w",     // white
			8 => "hx",    // bright black (gray)
			9 => "hr",    // bright red
			10 => "hg",   // bright green
			11 => "hy",   // bright yellow
			12 => "hb",   // bright blue
			13 => "hm",   // bright magenta
			14 => "hc",   // bright cyan
			15 => "hw",   // bright white
			_ => MapXtermColorTo16Color(xterm.Value, config)
		};
	}

	private static string MapXtermColorTo16Color(int xterm, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		// For colors beyond 16, find the closest match
		// Get the RGB value and map to closest basic color
		var colorMatch = config.Colors.Where(c => c.xterm == xterm).ToList();
		if (colorMatch.Count == 0 || colorMatch[0].rgb == null)
		{
			return "w"; // default to white
		}

		var rgb = colorMatch[0].rgb;
		var r = Convert.ToInt32(rgb.Substring(2, 2), 16);
		var g = Convert.ToInt32(rgb.Substring(4, 2), 16);
		var b = Convert.ToInt32(rgb.Substring(6, 2), 16);

		// Simple brightness check
		var brightness = (r + g + b) / 3;
		var highlight = brightness > 128 ? "h" : "";

		// Determine dominant color
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
			return "x"; // black
		}
		else
		{
			return highlight + "w";
		}
	}

	private static List<string> ConvertColorToNames(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		var result = new List<string>();

		// If already a color name
		if (colorSpec.StartsWith('+'))
		{
			colorSpec = colorSpec[1..];
		}

		if (config.ColorsByName.TryGetValue(colorSpec, out var color))
		{
			// Find all colors with the same RGB value
			if (config.ColorsByRgb.TryGetValue(color.rgb, out var colors))
			{
				result.AddRange(colors.Select(c => c.name));
			}
			return result;
		}

		// Try hex format
		if (colorSpec.StartsWith('#'))
		{
			var rgb = "0x" + colorSpec[1..];
			if (config.ColorsByRgb.TryGetValue(rgb, out var colors))
			{
				result.AddRange(colors.Select(c => c.name));
			}
			return result;
		}

		// Try xterm format
		if (int.TryParse(colorSpec, out var xtermNum) || 
			(colorSpec.StartsWith("xterm") && int.TryParse(colorSpec[5..], out xtermNum)))
		{
			var xtermColors = config.Colors.Where(c => c.xterm == xtermNum).ToList();
			if (xtermColors.Count > 0)
			{
				// Get all colors with the same RGB as the first match
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
	public async ValueTask<CallState> Motd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/connect
		var motdData = await _objectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.ConnectMotd ?? string.Empty);
	}

	[SharpFunction(Name = "wizmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public async ValueTask<CallState> WizMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/wizard
		var motdData = await _objectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.WizardMotd ?? string.Empty);
	}

	[SharpFunction(Name = "downmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public async ValueTask<CallState> DownMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/down
		var motdData = await _objectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.DownMotd ?? string.Empty);
	}

	[SharpFunction(Name = "fullmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public async ValueTask<CallState> FullMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Returns the current @motd/full
		var motdData = await _objectDataService!.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.FullMotd ?? string.Empty);
	}

	[SharpFunction(Name = "CONFIG", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular, 
		ParameterNames = ["option"])]
	public ValueTask<CallState> Config(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		
		// Use generated ConfigMetadata to get all option names
		var allOptionNames = ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Keys;
		
		if (!args.TryGetValue("0", out var optionArg) || string.IsNullOrWhiteSpace(optionArg.Message?.ToPlainText()))
		{
			// Return list of config option names
			var optionNames = allOptionNames
				.Select(prop => ConfigGenerated.ConfigMetadata.PropertyToAttributeName[prop].ToLowerInvariant())
				.OrderBy(n => n);
			return ValueTask.FromResult<CallState>(string.Join(" ", optionNames));
		}

		var searchTerm = optionArg.Message!.ToPlainText();
		
		// Find matching property by attribute name (case-insensitive)
		var matchingProperty = ConfigGenerated.ConfigMetadata.PropertyToAttributeName
			.FirstOrDefault(kvp => kvp.Value.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));
		
		if (matchingProperty.Key != null)
		{
			var value = ConfigGenerated.ConfigAccessor.GetValue(_configuration!.CurrentValue, matchingProperty.Key);
			return ValueTask.FromResult<CallState>(value?.ToString() ?? "");
		}
		
		return ValueTask.FromResult<CallState>("#-1 NO SUCH OPTION");
	}
}