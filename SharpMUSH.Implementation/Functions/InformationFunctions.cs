using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
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
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Reality;
using DotNext;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "accname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public async ValueTask<CallState> AccName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var accnameAttr = await AttributeService.GetAttributeAsync(
					executor, found, "ACCNAME", IAttributeService.AttributeMode.Read);

				if (accnameAttr is SharpAttribute[] chain)
				{
					var attr = chain.Last();
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
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		AnySharpObject targetPlayer = executor;
		string? folderSpec = null;

		if (args.Count == 0)
		{
			folderSpec = await Implementation.Commands.MailCommand.MessageListHelper.CurrentMailFolder(
				parser, ObjectDataService, executor);
		}
		else if (args.Count == 1)
		{
			var arg = args["0"].Message!.ToPlainText();

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

				var locateResult = await LocateService.LocateAndNotifyIfInvalid(
					parser, executor, executor, arg, LocateFlags.PlayersPreference);

				if (locateResult is AnySharpObject and SharpPlayer located)
				{
					targetPlayer = located;
					folderSpec = await Implementation.Commands.MailCommand.MessageListHelper.CurrentMailFolder(
						parser, ObjectDataService, targetPlayer);
				}
				else
				{
					folderSpec = arg;
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
			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

			if (locateResult is not (AnySharpObject and SharpPlayer located))
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			targetPlayer = located;
			folderSpec = args["1"].Message!.ToPlainText();
		}

		// Only a player has a mailbox; anything else holds no mail.
		var tally = targetPlayer is SharpPlayer mailbox
			? await TallyMail(Mediator.CreateStream(new GetMailListQuery(mailbox, folderSpec ?? "INBOX")))
			: new MailTally();

		return new CallState($"{tally.Read} {tally.Unread} {tally.Cleared}");
	}


	[SharpFunction(Name = "restarts", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> restarts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();
		return uptimeData?.Reboots ?? 0;
	}

	/// <remarks>
	/// PennMUSH returns this in time() format, not as a number (game/txt/hlp/pennfunc.hlp).
	/// uptime(reboot) is the numeric form.
	/// </remarks>
	[SharpFunction(Name = "restarttime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> RestartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var uptimeData = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();
		return (uptimeData?.LastRebootTime ?? DateTimeOffset.Now)
			.ToLocalTime()
			.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);
	}

	[SharpFunction(Name = "pidinfo", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["pid", "field", "delimiter"])]
	public async ValueTask<CallState> PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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

		var task = await Mediator.CreateStream(new ScheduleSemaphoreQuery(pid), ExecutionBudget.CurrentToken).FirstOrDefaultAsync(ExecutionBudget.CurrentToken);
		if (task is null) return new CallState(ErrorMessages.Returns.NoSuchPid);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		try
		{
			if (!await parser.ServiceProvider.GetRequiredService<SharpMUSH.Library.Services.IQueueControlService>()
				.CanAccessLegacyAsync(executor, pid, mutate: false, ExecutionBudget.CurrentToken))
				return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
		catch (NotSupportedException)
		{
			return new CallState(ErrorMessages.Returns.ErrorNotSupported);
		}
		return FormatTaskInfo(task, field, delimiter);
	}

	private CallState FormatTaskInfo(SemaphoreTaskData task, string? field, string delimiter)
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
	public async ValueTask<CallState> Alias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var args = parser.CurrentState.Arguments;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found =>
			{
				var aliases = found.Aliases;

				if (args.Count == 1)
				{
					return ValueTask.FromResult(new CallState(aliases.FirstOrDefault() ?? string.Empty));
				}

				var indexArg = args["1"].Message!.ToPlainText();
				if (!int.TryParse(indexArg, out var index) || index < 1)
				{
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidAliasIndex));
				}

				return ValueTask.FromResult(new CallState(
					index <= aliases.Length
						? aliases[index - 1]
						: string.Empty));
			});
	}

	[SharpFunction(Name = "findable", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "looker"])]
	public async ValueTask<CallState> Findable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var lookerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var targetArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		var maybeLooker = await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, lookerArg, LocateFlags.All);

		return maybeLooker switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject looker => new CallState(
				await LocateService.Locate(parser, looker, executor, targetArg, LocateFlags.All) is AnySharpObject)
		};
	}

	[SharpFunction(Name = "fullalias", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> FullAlias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found =>
			{
				return ValueTask.FromResult(new CallState(string.Join(" ", found.Aliases)));
			});
	}

	[SharpFunction(Name = "fullname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> FullName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found =>
			{
				var name = found.Object().Name;
				return ValueTask.FromResult(new CallState(
					string.Join(" ", found switch
					{
						SharpExit exit => [name, .. exit.Aliases ?? []],
						SharpPlayer or SharpRoom or SharpThing => [name]
					})));
			});
	}

	[SharpFunction(Name = "getpids", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["type"])]
	public async ValueTask<CallState> GetProcessIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		if (HelperFunctions.SplitDbRefAndOptionalAttr(arg0) is not { Object: var db, Attribute: var attr })
		{
			return string.Format(ErrorMessages.Returns.BadArgumentFormat, "getpids");
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, db, LocateFlags.All,
			async found =>
			{
				var query = attr is null
					? new ScheduleSemaphoreQuery(found.Object().DBRef)
					: new ScheduleSemaphoreQuery(new DbRefAttribute(found.Object().DBRef, attr.Split('`')));
				var pids = Mediator.CreateStream(query).Select(x => x.Pid);
				return string.Join(' ', await pids.ToArrayAsync());
			});
	}

	[SharpFunction(Name = "powers", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, SideEffectMinArgs = 2, ParameterNames = ["object"])]
	public async ValueTask<CallState> Powers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var obj);
		parser.CurrentState.Arguments.TryGetValue("1", out var power);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		switch (parser.CurrentState.Arguments.Count)
		{
			case 0:
				return string.Join(' ', await Mediator.CreateStream(new GetPowersQuery()).Select(x => x.Name).ToArrayAsync());

			case 1:
				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, obj!.Message!.ToPlainText(), LocateFlags.All,
					async found => string.Join(' ', await found.Object().Powers.Value.Select(x => x.Name).ToArrayAsync()));

			default:
				{

					// PennMUSH src/fundb.c fun_powers hands the side-effect form straight to do_power, so it
					// gets the same wizard-only check and the same "!" revoke handling as @power.
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
						parser, executor, executor, obj!.Message!.ToPlainText(), LocateFlags.All,
						async found =>
							await ManipulateSharpObjectService.SetOrUnsetPowers(executor, found,
								power!.Message!.ToPlainText(), true));
				}
		}
	}

	[SharpFunction(Name = "haspower", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "power"])]
	public async ValueTask<CallState> HasPower(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var toLocate = parser.CurrentState.Arguments["0"].Message!;
		var power = (parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty).ToPlainText().ToUpper();
		var maybeLocate = await
			LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, toLocate.ToPlainText(),
				LocateFlags.All);

		return maybeLocate switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject located => new CallState(await located.HasPower(power))
		};
	}

	/// <summary>
	/// <c>isapproved(&lt;object&gt;)</c> — 1 when the object is royalty or above, or carries the
	/// <c>APPROVED</c> flag; 0 otherwise (and always 0 for a guest). The softcode face of
	/// <see cref="HelperFunctions.IsApproved"/>, so a game's <c>+</c>-verbs and the engine answer the
	/// approval question with the same code rather than two copies of the same rule.
	/// </summary>
	[SharpFunction(Name = "isapproved", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> IsApproved(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var toLocate = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, toLocate, LocateFlags.All,
			async found => new CallState(await found.IsApproved()));
	}

	[SharpFunction(Name = "hastype", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "type"])]
	public async ValueTask<CallState> HasType(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var toLocate = parser.CurrentState.Arguments["0"].Message!;
		var typeQuery = (parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty).ToPlainText().ToUpper().Split(" ");
		var maybeLocate = await
			LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, toLocate.ToPlainText(),
				LocateFlags.All);

		return maybeLocate switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject when typeQuery.Any(x => x is not "PLAYER" and not "THING" and not "ROOM" and not "EXIT")
				=> new CallState(ErrorMessages.Returns.NoSuchType),
			AnySharpObject located => new CallState(typeQuery.Any(validType => located.HasType(validType)))
		};
	}

	[SharpFunction(Name = "iname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> IName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found => ValueTask.FromResult(new CallState(found.Object().Name)));
	}

	[SharpFunction(Name = "lpids", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public async ValueTask<CallState> LPIDs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var target = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, "me");
		var queueTypesStr = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "wait semaphore").ToPlainText()
			.ToUpperInvariant();

		var queueTypes = queueTypesStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

		if (queueTypes.Any(type => type is not "WAIT" and not "SEMAPHORE" and not "INDEPENDENT"))
		{
			return new CallState(ErrorMessages.Returns.InvalidQueueType);
		}

		var maybeLocate = await
			LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, target.ToPlainText(),
				LocateFlags.All);

		return maybeLocate switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject located => await PidsOf(located.Object().DBRef)
		};

		async ValueTask<CallState> PidsOf(DBRef locationDBRef)
		{
			bool includeWait = queueTypes.Contains("WAIT");
			bool includeSemaphore = queueTypes.Contains("SEMAPHORE");
			bool independent = queueTypes.Contains("INDEPENDENT");

			if (!includeWait && !includeSemaphore)
			{
				includeWait = true;
				includeSemaphore = true;
			}

			var allPids = AsyncEnumerable.Empty<long>();

			if (includeWait)
			{
				allPids = allPids.Concat(Mediator.CreateStream(new ScheduleDelayQuery(locationDBRef)));
			}

			if (includeSemaphore)
			{
				allPids = allPids.Concat(Mediator.CreateStream(new ScheduleSemaphoreQuery(locationDBRef)).Select(x => x.Pid));
			}

			// Note: INDEPENDENT filtering would require owner-based filtering
			// Current implementation returns PIDs for the specific DBRef
			// In PennMUSH, INDEPENDENT filters out tasks from objects with same owner but different DBRef
			// This would require extending the query to check task executor owner vs target owner

			return new CallState(string.Join(' ', await allPids.OrderBy(x => x).ToArrayAsync()));
		}
	}

	[SharpFunction(Name = "lstats", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["type"])]
	public async ValueTask<CallState> LStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var typeFilter = args.TryGetValue("0", out var typeArg)
			? typeArg.Message!.ToPlainText().ToUpperInvariant()
			: null;

		// One pass over the database, counting each type as it streams by.
		var countByType = new Dictionary<string, int>();
		await foreach (var obj in Mediator.CreateStream(new GetAllObjectsQuery()))
		{
			CollectionsMarshal.GetValueRefOrAddDefault(countByType, obj.Type, out _)++;
		}

		var players = countByType.GetValueOrDefault("PLAYER");
		var things = countByType.GetValueOrDefault("THING");
		var exits = countByType.GetValueOrDefault("EXIT");
		var rooms = countByType.GetValueOrDefault("ROOM");
		const int garbage = 0; // SharpMUSH doesn't track garbage separately

		if (!string.IsNullOrEmpty(typeFilter))
		{
			var count = typeFilter switch
			{
				"PLAYER" or "PLAYERS" => players,
				"THING" or "THINGS" => things,
				"EXIT" or "EXITS" => exits,
				"ROOM" or "ROOMS" => rooms,
				"GARBAGE" => garbage,
				_ => -1
			};

			return count >= 0 ? new CallState(count.ToString()) : new CallState(ErrorMessages.Returns.InvalidType);
		}

		return new CallState($"{players} {things} {exits} {rooms} {garbage}");
	}

	[SharpFunction(Name = "money", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Money(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MoneyFunctionNotSupported), executor);
		return new CallState(ErrorMessages.Returns.ErrorNotSupported);
	}

	[SharpFunction(Name = "mudname", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> MudName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Configuration.CurrentValue.Net.MudName);

	[SharpFunction(Name = "mudurl", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> MudURL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Configuration.CurrentValue.Net.MudUrl ?? "");

	/// <summary>
	/// locale() — returns the BCP-47 locale tag that is active on the executor's current
	/// connection (e.g. "en", "fr"). Returns "en" when no locale has been set.
	/// </summary>
	[SharpFunction(Name = "locale", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> LocaleFunc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var handle = parser.CurrentState.Handle;
		if (handle is null)
			return ValueTask.FromResult(new CallState("en"));

		var conn = ConnectionService.Get(handle.Value);
		if (conn is null)
			return ValueTask.FromResult(new CallState("en"));

		conn.Metadata.TryGetValue("Locale", out var locale);
		return ValueTask.FromResult(new CallState(string.IsNullOrEmpty(locale) ? "en" : locale));
	}

	[SharpFunction(Name = "name", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, SideEffectMinArgs = 2, ParameterNames = ["object", "new name"])]
	public async ValueTask<CallState> Name(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var newName = parser.CurrentState.Arguments.GetValueOrDefault("1");

		if (newName is null)
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, obj, LocateFlags.All,
				found => found.Object().Name);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found => await ManipulateSharpObjectService.SetName(executor, found, newName.Message!, true));
	}

	[SharpFunction(Name = "moniker", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Moniker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found => new CallState(await new SharpMUSH.Library.Services.NameFormatter(AttributeService, Configuration)
				.FormatAsync(found, SharpMUSH.Library.Services.NameContext.AccentedMoniker)));
	}

	/// <summary>
	/// PennMUSH <c>fun_nearby</c> (src/fundb.c:896). Three things about the shape are load-bearing:
	/// <list type="number">
	///   <item>The answer is <c>nearby()</c> — immediate locations plus the two carrying cases — and not
	///     "same enclosing room". A coin in a bag in the Tavern is <em>not</em> nearby a player standing
	///     in the Tavern, because the coin's <c>where_is</c> is the bag.</item>
	///   <item>The permission gate comes first, so an executor with no standing gets
	///     <c>#-1 NO OBJECTS CONTROLLED</c> rather than being told whether the argument resolved.</item>
	///   <item>An unresolvable argument answers the match's own error, where Penn writes a bare
	///     <c>#-1</c> and leaves the reason to <c>match_thing</c>'s notification.</item>
	/// </list>
	/// </summary>
	[SharpFunction(Name = "nearby", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object1", "object2"])]
	public async ValueTask<CallState> Nearby(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj1Arg = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var obj2Arg = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		// Both matches run before anything branches: Penn matches both, then gates, then reports.
		var maybeObj1 = await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, obj1Arg, LocateFlags.All);
		var maybeObj2 = await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, obj2Arg, LocateFlags.All);

		var obj1 = maybeObj1 is AnySharpObject found1 ? found1 : null;
		var obj2 = maybeObj2 is AnySharpObject found2 ? found2 : null;

		// controls() and nearby() are both false for NOTHING in Penn, so an argument that did not
		// resolve simply contributes nothing to the gate rather than skipping it.
		if (!await Standing(executor, obj1) && !await Standing(executor, obj2) && !await executor.IsSee_All())
		{
			return new CallState(ErrorMessages.Returns.NoObjectsControlled);
		}

		if (maybeObj1 is Error<CallState> missing1) return missing1.Value;
		if (maybeObj2 is Error<CallState> missing2) return missing2.Value;
		if (obj1 is null || obj2 is null) return new CallState(ErrorMessages.Returns.NoMatch);

		return new CallState(await Library.Services.LocateService.Nearby(obj1, obj2));

		async ValueTask<bool> Standing(AnySharpObject executor, AnySharpObject? obj)
			=> obj is not null
				 && (await PermissionService.Controls(executor, obj)
						 || await Library.Services.LocateService.Nearby(executor, obj));
	}

	[SharpFunction(Name = "playermem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public ValueTask<CallState> PlayerMem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(0);

	/// <summary>
	/// PennMUSH's fun_quota (src/wiz.c:1864-1896). It matches a player only, so a thing cannot stand in
	/// for its owner; it refuses unless <c>Do_Quotas(executor) || See_All(executor) || controls(executor,
	/// who)</c>, the gate @quota applies at wiz.c:179; and it answers one integer, 99999 for a No_Quota
	/// holder. Penn's <c>owned + get_current_quota(who)</c> is the player's limit, which SharpMUSH
	/// stores directly as <see cref="SharpPlayer.Quota"/>. Where Penn returns a bare <c>#-1</c> and
	/// notifies the reason, this returns the reason instead: the match's own error, or
	/// <c>#-1 PERMISSION DENIED</c>.
	/// </summary>
	[SharpFunction(Name = "quota", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public async ValueTask<CallState> Quota(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, QuotaMatchFlags) switch
		{
			AnySharpObject and SharpPlayer player => await QuotaOfAsync(executor, player),
			AnySharpObject => throw new InvalidOperationException("A player-only locate matched something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	/// <summary>
	/// wiz.c:1869 — <c>TYPE_PLAYER, MAT_TYPE | MAT_PMATCH | MAT_ME</c>. LocateService's player flag set
	/// models <c>lookup_player</c> alone, so <c>MAT_ME</c> is added here. <c>AbsoluteMatch</c> stays even
	/// though <c>MAT_ABSOLUTE</c> is absent: <c>MAT_PMATCH</c> goes through <c>lookup_player</c>, which
	/// takes a <c>#dbref</c> when it names a player (<c>src/plyrlist.c:169</c>).
	/// </summary>
	private const LocateFlags QuotaMatchFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.MatchMeForLooker |
		LocateFlags.AbsoluteMatch | LocateFlags.EnglishStyleMatching | LocateFlags.MatchOptionalWildCardForPlayerName;

	private async ValueTask<CallState> QuotaOfAsync(AnySharpObject executor, SharpPlayer player)
	{
		if (!await PermissionService.CanSeeQuota(executor, player))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return await player.Object.HasPower("No_Quota")
			? new CallState(99999)
			: new CallState(player.Quota);
	}

	[SharpFunction(Name = "type", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Type(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All, found => found.TypeString());
	}

	[SharpFunction(Name = "textsearch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public async ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var classArg = args["0"].Message!.ToPlainText();
		var pattern = args["1"].Message!.ToPlainText();
		var attributePattern = args.TryGetValue("2", out var attrArg)
			? attrArg.Message!.ToPlainText()
			: "*";

		AnySharpObject? classObj = null;
		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await LocateService.Locate(parser, executor, executor, classArg, LocateFlags.All);
			if (maybeClass is not AnySharpObject classFound)
			{
				return new CallState(ErrorMessages.Returns.InvalidClass);
			}
			classObj = classFound;
		}

		var results = Mediator.CreateStream(new GetAllObjectsQuery())
			.Where(async (obj, _) => classObj is null
				|| (await obj.Owner.WithCancellation(CancellationToken.None)).Object.DBRef == classObj.Object().DBRef)
			.Where(async (obj, _) => await obj.Attributes.Value.AnyAsync(attr =>
				(attributePattern == "*" || attr.Name.Contains(attributePattern, StringComparison.OrdinalIgnoreCase))
				&& attr.Value.ToPlainText().Contains(pattern, StringComparison.OrdinalIgnoreCase)))
			.Select(obj => new DBRef(obj.Key, obj.CreationTime).ToString());

		return new CallState(string.Join(" ", await results.ToArrayAsync()));
	}

	[SharpFunction(Name = "colors", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["ansi-string", "strip"])]
	public ValueTask<CallState> Colors(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
				.Distinct();

			return ValueTask.FromResult(new CallState(string.Join(" ", allColors)));
		}

		if (args.Count == 1 || (args.Count == 2 && string.IsNullOrEmpty(args["1"]?.Message?.ToPlainText())))
		{
			var wildcardPattern = args["0"].Message!.ToPlainText();
			var matchingColors = colorsConfig.Colors
				.Where(c => MushText.IsWildcardMatch(MarkupText.Plain(c.name), wildcardPattern))
				.Select(c => c.name)
				.Distinct();

			return ValueTask.FromResult(new CallState(string.Join(" ", matchingColors)));
		}

		var colorSpec = args["0"].Message!.ToPlainText();
		var formatSpec = args["1"].Message!.ToPlainText().ToLowerInvariant();

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
	private (string? foreground, string? background, string styles) ParseColorSpecification(string spec)
	{
		string? foreground = null;
		string? background = null;
		var styles = new List<char>();

		foreach (var part in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (part.StartsWith('/'))
			{
				background = part[1..];
				continue;
			}

			// The code letters run ahead of any named colour; each is a style, kept once.
			var codeLetters = part.AsSpan().IndexOfAnyExcept(AnsiCodeLetters) switch
			{
				-1 => part.Length,
				var end => end
			};

			foreach (var style in part.AsSpan(0, codeLetters))
			{
				if (!styles.Contains(style))
				{
					styles.Add(style);
				}
			}

			var colorPart = part[codeLetters..];
			if (!string.IsNullOrWhiteSpace(colorPart))
			{
				foreground = colorPart;
			}
		}

		return (foreground, background, new string(CollectionsMarshal.AsSpan(styles)));
	}

	/// <summary>The single letters an ansi() code spells its styles and colours with.</summary>
	private static readonly SearchValues<char> AnsiCodeLetters = SearchValues.Create("fuihxrgybmcwXRGYBMCW");

	private string FormatColorsAsHex(string? foreground, string? background, string styles, bool includeStyles,
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

	private string FormatColorsAsRgb(string? foreground, string? background, string styles, bool includeStyles,
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

	private string FormatColorsAsXterm(string? foreground, string? background, string styles, bool includeStyles,
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

	private string FormatColorsAs16Color(string? foreground, string? background, string styles, bool includeStyles,
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

	private string FormatColorsAsName(string? foreground, string? background, string styles, bool includeStyles,
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

	private string FormatColorsAsAuto(string originalSpec, string? foreground, string? background, string styles, bool includeStyles)
	{
		return originalSpec;
	}

	private string? ConvertColorToHex(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
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

		if (int.TryParse(colorSpec, out var xtermNum) && XtermColor(config, xtermNum) is { } xtermColor)
		{
			return "#" + xtermColor.rgb[2..];
		}

		if (colorSpec.StartsWith("xterm") && int.TryParse(colorSpec[5..], out var xtermNum2)
			&& XtermColor(config, xtermNum2) is { } xtermColor2)
		{
			return "#" + xtermColor2.rgb[2..];
		}

		return null;
	}

	/// <summary>The first configured colour at an xterm index, or null when the palette has none there.</summary>
	private static ColorIdentity? XtermColor(SharpMUSH.Configuration.Options.ColorsOptions config, int xterm)
		=> config.Colors.FirstOrDefault(c => c.xterm == xterm);

	private string? ConvertColorToRgb(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
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

	private int? ConvertColorToXterm(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
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

	private string? ConvertColorTo16Color(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
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

	private string MapXtermColorTo16Color(int xterm, SharpMUSH.Configuration.Options.ColorsOptions config)
	{
		if (XtermColor(config, xterm) is not { rgb: { } rgb })
		{
			return "w";
		}

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

	private List<string> ConvertColorToNames(string colorSpec, SharpMUSH.Configuration.Options.ColorsOptions config)
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

		if ((int.TryParse(colorSpec, out var xtermNum) ||
				(colorSpec.StartsWith("xterm") && int.TryParse(colorSpec[5..], out xtermNum)))
			&& XtermColor(config, xtermNum) is { } xtermColor
			&& config.ColorsByRgb.TryGetValue(xtermColor.rgb, out var xtermNames))
		{
			result.AddRange(xtermNames.Select(c => c.name));
		}

		return result;
	}

	[SharpFunction(Name = "motd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> Motd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.ConnectMotd ?? string.Empty);
	}

	[SharpFunction(Name = "wizmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public async ValueTask<CallState> WizMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.WizardMotd ?? string.Empty);
	}

	[SharpFunction(Name = "downmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public async ValueTask<CallState> DownMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.DownMotd ?? string.Empty);
	}

	[SharpFunction(Name = "fullmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = [])]
	public async ValueTask<CallState> FullMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.FullMotd ?? string.Empty);
	}

	[SharpFunction(Name = "CONFIG", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["option"])]
	public ValueTask<CallState> Config(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!args.TryGetValue("0", out var optionArg) || string.IsNullOrWhiteSpace(optionArg.Message?.ToPlainText()))
		{
			var optionNames = ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Values
				.Select(name => name.ToLowerInvariant())
				.Order();
			return ValueTask.FromResult<CallState>(string.Join(" ", optionNames));
		}

		var searchTerm = optionArg.Message!.ToPlainText();

		var matchingProperty = ConfigGenerated.ConfigMetadata.PropertyToAttributeName
			.FirstOrDefault(kvp => kvp.Value.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingProperty.Key != null)
		{
			var value = ConfigGenerated.ConfigAccessor.GetValue(Configuration.CurrentValue, matchingProperty.Key);
			return ValueTask.FromResult<CallState>(value?.ToString() ?? "");
		}

		return ValueTask.FromResult<CallState>(ErrorMessages.Returns.NoSuchOption);
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

	/// <summary>
	/// PennMUSH's <c>functions([&lt;type&gt;])</c> (<c>src/function.c</c> <c>list_functions</c>): the type is
	/// "builtin", "local" (@functions) or "all", and anything else is <c>#-1 INVALID ARGUMENT</c>.
	/// </summary>
	[SharpFunction(Name = "functions", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> FFunctions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var type = parser.CurrentState.Arguments.TryGetValue("0", out var arg0)
			? (arg0.Message ?? MarkupText.Empty).ToPlainText()
			: string.Empty;

		return ValueTask.FromResult(type is "" or "all" or "builtin" or "local"
			? new CallState(string.Join(' ', FunctionNames(parser, type)))
			: new CallState(ErrorMessages.Returns.InvalidArgument));
	}

	/// <summary>Function names of one type — "builtin", "local", or anything else for both — sorted, lower-case.</summary>
	private static IEnumerable<string> FunctionNames(IMUSHCodeParser parser, string type)
		=> (type switch
		{
			"builtin" => parser.FunctionLibrary.AsEnumerable().Where(kv => kv.Value.IsSystem),
			"local" => parser.FunctionLibrary.AsEnumerable().Where(kv => !kv.Value.IsSystem),
			_ => parser.FunctionLibrary.AsEnumerable()
		})
			.Select(kv => kv.Value.LibraryInformation.Attribute.Name)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
			.Select(s => s.ToLowerInvariant());

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
				return new CallState(JoinSpace(FunctionNames(parser, type)));
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

	private static async ValueTask<Func<DBRef, CancellationToken, ValueTask<bool>>> ObserveProjectionRealityAsync(
		IMUSHCodeParser parser, DBRef receiver)
	{
		var reality = parser.ServiceProvider.GetRequiredService<IRealityPolicy>();
		return reality is IRealityObservationProvider observations
			? await observations.ObserveAsync(receiver, ExecutionBudget.CurrentToken)
			: (target, token) => reality.CanPerceiveAsync(receiver, target, token);
	}

	[SharpFunction(Name = "valid", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["type", "name"])]
	public async ValueTask<CallState> Valid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var category = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var str = parser.CurrentState.Arguments["1"].Message!;
		var target = parser.CurrentState.Arguments.Count >= 3
			? parser.CurrentState.Arguments["2"].Message!.ToPlainText()
			: null;
		// valid() runs as the executor: its optional <target> is located, and the default validation
		// context, relative to the executor (PennMUSH matches function arguments to the executor) — not
		// the caller one frame up the u() chain.
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var validationType = category switch
		{
			"name" => IValidateService.ValidationType.Name,
			"attrname" => IValidateService.ValidationType.AttributeName,
			"attrvalue" => IValidateService.ValidationType.AttributeValue,
			"playername" => IValidateService.ValidationType.PlayerName,
			"password" => IValidateService.ValidationType.Password,
			"command" => IValidateService.ValidationType.CommandName,
			"function" => IValidateService.ValidationType.FunctionName,
			"flag" => IValidateService.ValidationType.FlagName,
			"qreg" => IValidateService.ValidationType.QRegisterName,
			"colorname" => IValidateService.ValidationType.ColorName,
			"ansicodes" => IValidateService.ValidationType.AnsiCode,
			"channel" => IValidateService.ValidationType.ChannelName,
			"timezone" => IValidateService.ValidationType.Timezone,
			"locktype" => IValidateService.ValidationType.LockType,
			"lockkey" => IValidateService.ValidationType.LockKey,
			_ => IValidateService.ValidationType.Invalid
		};

		if (validationType == IValidateService.ValidationType.Invalid)
		{
			return string.Format(ErrorMessages.Returns.BadArgumentFormat, "valid");
		}

		return validationType switch
		{
			IValidateService.ValidationType.AttributeValue when target is not null
				=> new CallState(await ValidateService.Valid(validationType, str, await GetAttributeEntry(target)) ? "1" : "0"),
			IValidateService.ValidationType.AttributeValue
				=> new CallState(await ValidateService.Valid(validationType, str, new None()) ? "1" : "0"),

			IValidateService.ValidationType.PlayerName when target is null
				=> new CallState(await ValidateService.Valid(validationType, str, executor) ? "1" : "0"),
			IValidateService.ValidationType.PlayerName
				when await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target, LocateFlags.All)
					is AnySharpObject obj
				=> new CallState(await ValidateService.Valid(validationType, str, obj) ? "1" : "0"),
			IValidateService.ValidationType.PlayerName => ErrorMessages.Returns.CantSeeThat,

			IValidateService.ValidationType.ChannelName
				=> new CallState(await ValidateService.Valid(validationType, str, await GetChannel(target ?? string.Empty)) ? "1" : "0"),

			IValidateService.ValidationType.LockType when target is null
				=> new CallState(await ValidateService.Valid(validationType, str, executor) ? "1" : "0"),
			IValidateService.ValidationType.LockType
				when await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target, LocateFlags.All)
					is AnySharpObject obj
				=> new CallState(await ValidateService.Valid(validationType, str, obj) ? "1" : "0"),
			IValidateService.ValidationType.LockType => ErrorMessages.Returns.CantSeeThat,
			_ => new CallState(await ValidateService.Valid(validationType, str, new None()) ? "1" : "0")
		};

		async ValueTask<ValidationTarget> GetChannel(string t)
		{
			var channel = await Mediator.Send(new GetChannelQuery(t));
			return channel is null
				? new None()
				: channel;
		}

		async ValueTask<ValidationTarget> GetAttributeEntry(string name)
		{
			var entry = await Mediator.Send(new GetAttributeEntryQuery(name));
			return entry is null
				? new None()
				: entry;
		}
	}

	[SharpFunction(Name = "version", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> Version(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Implementation.Generated.VersionInfo.Version);

	[SharpFunction(Name = "poll", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> Poll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var pollData = await ObjectDataService.GetExpandedServerDataAsync<PollData>();
		return new CallState(pollData?.Message ?? string.Empty);
	}
}
