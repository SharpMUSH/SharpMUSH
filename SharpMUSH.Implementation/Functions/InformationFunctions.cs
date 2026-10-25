using SharpMUSH.Library.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Dice;
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
using SharpMUSH.Library.Services;
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
		var obj = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found => new CallState(await new SharpMUSH.Library.Services.NameFormatter(AttributeService, Configuration)
				.FormatAsync(found, SharpMUSH.Library.Services.NameContext.Accented)));
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
			var arg = args["0"].Message.ToPlainText();

			// A folder number is a strict integer (src/extmail.c:2057).
			if (ArgHelpers.TryStrictInteger(arg, out int folderNum) && folderNum >= 0 && folderNum <= 15)
			{
				folderSpec = arg;
			}
			else if (arg.All(char.IsUpper) || arg.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
			{
				folderSpec = arg;
			}
			else
			{
				if (!await executor.Can(PortalPermission.ChatAdmin))
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
			if (!await executor.Can(PortalPermission.ChatAdmin))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var playerArg = args["0"].Message.ToPlainText()!;
			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

			if (locateResult is not (AnySharpObject and SharpPlayer located))
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			targetPlayer = located;
			folderSpec = args["1"].Message.ToPlainText();
		}

		// Only a player has a mailbox; anything else holds no mail.
		// A folder is named by its number, as count_mail takes it, or by one of the player's names for it.
		var tally = targetPlayer is SharpPlayer mailbox
			? await TallyMail(Mediator.CreateStream(new GetMailListQuery(mailbox,
				Implementation.Commands.MailCommand.MailFolders.Resolve(
					await Implementation.Commands.MailCommand.MailFolders.LoadAsync(ObjectDataService, mailbox),
					folderSpec ?? "0") is Implementation.Commands.MailCommand.MailFolder folder
					? folder.Name
					: folderSpec ?? "INBOX")))
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

	/// <remarks>
	/// <c>fun_pidinfo</c> (<c>src/cque.c:1738-1819</c>): the fields asked for, in the order asked, joined
	/// by the output separator. A field is named by any prefix of its name; an unknown one is skipped.
	/// </remarks>
	[SharpFunction(Name = "pidinfo", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["pid", "fields", "delimiter"])]
	public async ValueTask<CallState> PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var pidStr = args["0"].Message.ToPlainText();

		// fun_pidinfo refuses anything but a strict unsigned integer with e_uint (src/cque.c:1747-1749).
		if (!ArgHelpers.TryStrictUnsignedLong(pidStr, out var pid))
		{
			return new CallState(ErrorMessages.Returns.UInteger);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		QueueEntrySnapshot? entry;
		try
		{
			entry = parser.ServiceProvider.GetRequiredService<ITaskQueueReader>().GetQueueEntry(pid);
			if (entry is null) return new CallState(ErrorMessages.Returns.InvalidPid);
			if (!await parser.ServiceProvider.GetRequiredService<SharpMUSH.Library.Services.IQueueControlService>()
				.CanAccessLegacyAsync(executor, pid, mutate: false, ExecutionBudget.CurrentToken))
				return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
		catch (NotSupportedException)
		{
			return new CallState(ErrorMessages.Returns.ErrorNotSupported);
		}

		var fields = args.TryGetValue("1", out var fieldArg) && fieldArg.Message.ToPlainText().Trim() is { Length: > 0 } asked
			? asked
			: "queue player time object attribute command";
		var delimiter = args.TryGetValue("2", out var delimiterArg) ? delimiterArg.Message.ToPlainText() : " ";

		var values = fields.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(field => PidField(entry, field))
			.OfType<string>();
		return new CallState(string.Join(delimiter, values));
	}

	/// <summary>One <c>pidinfo()</c> field, matched as <c>string_prefix(&lt;name&gt;, field)</c> matches it.</summary>
	private static string? PidField(QueueEntrySnapshot entry, string field)
	{
		bool Names(string name) => name.StartsWith(field, StringComparison.OrdinalIgnoreCase);

		if (Names("queue")) return entry.Semaphore is null ? "wait" : "semaphore";
		if (Names("player") || Names("executor")) return $"#{entry.Source?.Number ?? -1}";
		if (Names("time"))
			return entry is { Timed: true, RemainingDelay: { } remaining }
				? ((long)remaining.TotalSeconds).ToString(CultureInfo.InvariantCulture)
				: "-1";
		if (Names("object")) return $"#{entry.Semaphore?.DbRef.Number ?? -1}";
		if (Names("attribute")) return entry.Semaphore is { } semaphore ? string.Join('`', semaphore.Attribute) : "#-1";
		if (Names("command")) return entry.Command?.ToPlainText() ?? string.Empty;
		return null;
	}

	/// <summary>
	/// PennMUSH's <c>fun_alias</c> (<c>src/fundb.c:1760-1787</c>): the object's short alias - its alias list
	/// up to the first <c>;</c> (<c>shortalias</c>) - or, with a second argument, sets the ALIAS attribute
	/// as <c>@alias</c> does, clearing it when that argument is empty. Setting returns nothing; a player's
	/// alias write reports itself.
	/// </summary>
	[SharpFunction(Name = "alias", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		SideEffectMinArgs = 2, ParameterNames = ["object", "new alias"])]
	public async ValueTask<CallState> Alias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;
		var newAlias = parser.CurrentState.Arguments.GetValueOrDefault("1");

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				if (newAlias is null)
				{
					return new CallState(PlayerAliases.Short(await FullAliasOf(found)));
				}

				var value = newAlias.Message;
				_ = value.Length > 0
					? await AttributeService.SetAttributeAsync(executor, found, PlayerAliases.AttributeName, value)
					: await AttributeService.ClearAttributeAsync(executor, found, PlayerAliases.AttributeName,
						IAttributeService.AttributePatternMode.Exact);
				return CallState.Empty;
			});
	}

	/// <summary>
	/// PennMUSH's <c>fullalias</c> (<c>src/utils.c:645</c>): an exit's aliases are the rest of its name, and
	/// anything else's are its own ALIAS attribute, read without permission or inheritance as
	/// <c>atr_get_noparent</c> reads it.
	/// </summary>
	private async ValueTask<string> FullAliasOf(AnySharpObject found)
		=> found is SharpExit exit
			? string.Join(PlayerAliases.Delimiter, exit.Aliases ?? [])
			: (await Mediator.CreateStream(new GetAttributeQuery(found.Object().DBRef, [PlayerAliases.AttributeName]))
				.LastOrDefaultAsync())?.Value.ToPlainText() ?? string.Empty;

	/// <summary>
	/// <c>fun_findable</c> (<c>fundb.c:1438-1452</c>): whether the first object could find the second.
	/// Both are resolved as the executor, the way <c>match_thing(executor, ...)</c> does, and the
	/// answer is gated on <c>See_All(executor) || controls(executor, obj) || controls(executor,
	/// victim)</c>. That gate is the function's own — the matcher has none (#1222), and without it
	/// here findable() would answer for anyone about anyone.
	/// </summary>
	[SharpFunction(Name = "findable", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public async ValueTask<CallState> Findable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var lookerArg = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;
		var targetArg = parser.CurrentState.Arguments["1"].Message.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, lookerArg, LocateFlags.All) switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject looker => await FindableVictim(parser, executor, looker, targetArg)
		};
	}

	/// <summary>The victim half of <see cref="Findable"/>, once the object asked about has resolved.</summary>
	private async ValueTask<CallState> FindableVictim(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject looker, string targetArg)
		=> await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, targetArg, LocateFlags.All) switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject victim => await FindableAnswer(executor, looker, victim)
		};

	/// <summary>
	/// <c>safe_boolean(Can_Locate(obj, victim))</c> behind the gate (<c>fundb.c:1447-1451</c>). Both
	/// objects are already resolved, and the answer is about those two objects: <c>fun_findable</c>
	/// never matches a name a second time, so a context-sensitive argument (<c>me</c>, <c>here</c>)
	/// cannot mean one thing to the gate and another to the answer, and two objects sharing a name
	/// cannot be swapped between them.
	/// </summary>
	private async ValueTask<CallState> FindableAnswer(AnySharpObject executor, AnySharpObject looker,
		AnySharpObject victim)
	{
		if (!await executor.IsSee_All()
				&& !await PermissionService.Controls(executor, looker)
				&& !await PermissionService.Controls(executor, victim))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return new CallState(await PermissionService.CanLocate(looker, victim));
	}

	[SharpFunction(Name = "fullalias", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> FullAlias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found => new CallState(await FullAliasOf(found)));
	}

	[SharpFunction(Name = "fullname", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> FullName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			found =>
			{
				var name = found.Object().Name;
				return ValueTask.FromResult(new CallState(
					string.Join(PlayerAliases.Delimiter, found switch
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
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;

		if (HelperFunctions.SplitDbRefAndOptionalAttr(arg0) is not { Object: var db, Attribute: var attr })
		{
			return string.Format(ErrorMessages.Returns.BadArgumentFormat, "getpids");
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, db, LocateFlags.All,
			async found =>
			{
				if (!await CanSeeQueueOf(executor, found))
				{
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}

				var query = attr is null
					? new ScheduleSemaphoreQuery(found.Object().DBRef)
					: new ScheduleSemaphoreQuery(new DbRefAttribute(found.Object().DBRef, attr.Split('`')));
				var pids = Mediator.CreateStream(query).Select(x => x.Pid);
				return string.Join(' ', await pids.ToArrayAsync());
			});
	}

	/// <summary>@ps's rule for reading an object's queue: its controller, a privileged player or the See_Queue power.</summary>
	private async ValueTask<bool> CanSeeQueueOf(AnySharpObject executor, AnySharpObject located)
		=> await PermissionService.Controls(executor, located) || await executor.IsPriv() || await executor.HasPower("SEE_QUEUE");

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
					parser, executor, executor, obj!.Message.ToPlainText(), LocateFlags.All,
					async found => string.Join(' ', (await found.Object().ReadPowersAsync(ExecutionBudget.CurrentToken)).Select(x => x.Name)));

			default:
				{

					// PennMUSH src/fundb.c fun_powers hands the side-effect form straight to do_power, so it
					// gets the same wizard-only check and the same "!" revoke handling as @power.
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
						parser, executor, executor, obj!.Message.ToPlainText(), LocateFlags.All,
						async found =>
							await FlagAndPowerService.SetOrUnsetPowers(executor, found,
								power!.Message.ToPlainText(), true));
				}
		}
	}

	[SharpFunction(Name = "haspower", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "power"])]
	public async ValueTask<CallState> HasPower(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var toLocate = parser.CurrentState.Arguments["0"].Message;
		var power = parser.CurrentState.Arguments["1"].Message.ToPlainText().ToUpper();
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
	/// <c>isapproved(&lt;object&gt;)</c> — 1 when the object is royalty or above, or holds the
	/// <c>approved</c> role; 0 otherwise (and always 0 for a guest). The softcode face of
	/// <see cref="HelperFunctions.IsApproved"/>, so a game's <c>+</c>-verbs and the engine answer the
	/// approval question with the same code rather than two copies of the same rule.
	/// </summary>
	[SharpFunction(Name = "isapproved", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> IsApproved(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var toLocate = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, toLocate, LocateFlags.All,
			async found => new CallState(await found.IsApproved()));
	}

	[SharpFunction(Name = "hastype", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "type"])]
	public async ValueTask<CallState> HasType(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var toLocate = parser.CurrentState.Arguments["0"].Message;
		var typeQuery = parser.CurrentState.Arguments["1"].Message.ToPlainText().ToUpper().Split(" ");
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
		var obj = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found => await MaySeeInsideName(executor, found)
				? new CallState(await InsideNameAsync(parser, executor, found))
				: new CallState(ErrorMessages.Returns.PermissionDenied));
	}

	/// <summary>
	/// <c>fun_iname</c> (<c>src/fundb.c:1868-1890</c>): the caller controls the object, is See_All, or
	/// stands inside it.
	/// </summary>
	private async ValueTask<bool> MaySeeInsideName(AnySharpObject executor, AnySharpObject found)
		=> await PermissionService.Controls(executor, found)
			|| await executor.IsSee_All()
			|| (executor.AsOptionalContent is AnySharpContent content
				&& (await content.Location()).Object().DBRef == found.Object().DBRef);

	/// <summary>
	/// <c>nameformat()</c> (<c>src/unparse.c:175-195</c>) with the accented name as the default: the
	/// object's NAMEFORMAT, ignoring permissions, given the dbref and that name. A NAMEFORMAT that is
	/// there is shown as it evaluates, empty or not; only a missing one gives the plain name.
	/// </summary>
	private async ValueTask<MString> InsideNameAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject found)
	{
		var defaultName = found.IsExit
			? MString.Plain(found.Object().Name)
			: await new NameFormatter(AttributeService, Configuration).FormatAsync(found, NameContext.Accented);

		var god = await HelperFunctions.GetGod(Mediator);
		if (await AttributeService.GetAttributeAsync(god, found, "NAMEFORMAT",
				IAttributeService.AttributeMode.Read, true) is not SharpAttribute[])
		{
			return defaultName;
		}

		return await AttributeService.EvaluateAttributeFunctionAsync(parser, executor, found, "NAMEFORMAT",
			CallbackArguments.Of(("object", found.Object().DBRef.ToString()), ("name", defaultName)),
			evalParent: true, ignorePermissions: true);
	}

	/// <remarks>
	/// <c>fun_lpids</c> (<c>src/cque.c:1825-1923</c>): the wait queue's pids, then the semaphore
	/// queue's, of every object owned by the player's owner. <c>independent</c> keeps only the object's
	/// own, and <c>all</c>, or no object for a caller who can see the whole queue, lists every one.
	/// </remarks>
	[SharpFunction(Name = "lpids", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "types"])]
	public async ValueTask<CallState> LPIDs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var target = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, string.Empty).ToPlainText();
		var types = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, string.Empty).ToPlainText()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		var lookQueue = await executor.IsPriv() || await executor.HasPower("SEE_QUEUE");
		AnySharpObject? player = null;
		if (target.Equals("all", StringComparison.OrdinalIgnoreCase) || target.Length == 0)
		{
			player = lookQueue ? null : executor;
		}
		else
		{
			switch (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, target, LocateFlags.All))
			{
				case Error<CallState>:
					return new CallState(ErrorMessages.Returns.NotVisible);
				case AnySharpObject found when !lookQueue && !await OwnsOrControls(found):
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				case AnySharpObject found:
					player = found;
					break;
			}
		}

		if (types.Any(type => !type.Equals("wait", StringComparison.OrdinalIgnoreCase)
			&& !type.Equals("semaphore", StringComparison.OrdinalIgnoreCase)
			&& !type.Equals("independent", StringComparison.OrdinalIgnoreCase)))
		{
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var wait = types.Contains("wait", StringComparer.OrdinalIgnoreCase);
		var semaphore = types.Contains("semaphore", StringComparer.OrdinalIgnoreCase);
		if (!wait && !semaphore) wait = semaphore = true;
		var independent = types.Contains("independent", StringComparer.OrdinalIgnoreCase);

		var playerRef = player?.Object().DBRef;
		var playerOwner = player is null ? (DBRef?)null : (await player.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;

		IReadOnlyList<QueueEntrySnapshot> entries;
		try
		{
			entries = parser.ServiceProvider.GetRequiredService<ITaskQueueReader>().GetQueueEntries();
		}
		catch (NotSupportedException)
		{
			return new CallState(ErrorMessages.Returns.ErrorNotSupported);
		}

		// Penn lists qwait and qsemfirst: entries still waiting, not ones already moved to the run queue.
		var listed = new List<QueueEntrySnapshot>();
		var owners = new Dictionary<int, int>();
		foreach (var entry in entries.Where(entry => entry.Kind is "delay" or "semaphore"
			&& entry.State is QueueEntryState.Pending or QueueEntryState.Paused))
		{
			if (await Belongs(entry)) listed.Add(entry);
		}

		var waitPids = wait
			? listed.Where(entry => entry.Kind == "delay").OrderBy(entry => entry.RemainingDelay).Select(entry => entry.Pid)
			: [];
		var semaphorePids = semaphore
			? listed.Where(entry => entry.Kind == "semaphore").Select(entry => entry.Pid)
			: [];
		return new CallState(string.Join(' ', waitPids.Concat(semaphorePids)));

		// An entry whose executor is gone is listed for everyone, as Penn's GoodObject test leaves it in.
		async ValueTask<bool> Belongs(QueueEntrySnapshot entry)
		{
			if (playerRef is null || entry.Source is not { } source) return true;
			if (independent) return source.Number == playerRef.Value.Number;
			return await OwnerOf(source) is not { } owner || owner == playerOwner!.Value.Number;
		}

		async ValueTask<int?> OwnerOf(DBRef source)
		{
			if (owners.TryGetValue(source.Number, out var known)) return known;
			if (await Mediator.Send(new GetObjectNodeQuery(source)) is not AnySharpObject sourceObject) return null;
			var owner = (await sourceObject.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number;
			owners[source.Number] = owner;
			return owner;
		}

		async ValueTask<bool> OwnsOrControls(AnySharpObject found)
			=> (await found.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number
				== (await executor.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number
				|| await PermissionService.Controls(executor, found);
	}

	/// <summary>
	/// <c>@stats</c> as a function: the object census over the whole database, or over one player's
	/// objects.
	/// </summary>
	/// <remarks>
	/// <c>fun_lstats</c> (<c>src/fundb.c:2376-2405</c>) takes a <em>player</em>: absent, empty or
	/// <c>all</c> is <c>ANY_OWNER</c>, <c>me</c> is the executor, and anything else goes through
	/// <c>lookup_player</c>, which resolves a name or a <c>#dbref</c> and insists on a player — a
	/// miss being <c>e_notvis</c>. The argument is never an object <em>type</em>: a word such as
	/// <c>player</c> or <c>room</c> is just a name, and names no player.
	///
	/// <para>The gate is <c>Search_All(executor) || who == ANY_OWNER || controls(executor, who)</c>
	/// (<c>:2396-2398</c>). Note which way the middle term runs: a mortal asking for the whole
	/// database's figures is allowed, and it is asking about somebody else in particular that needs
	/// the warrant. That is a different rule from <c>do_stats</c>'s <c>owner != player</c>
	/// (<c>src/wiz.c:770-774</c>), which is why <c>@stats</c> keeps its own.</para>
	///
	/// <para>The whole-database form is six fields and the one-player form five — PennMUSH's
	/// one-player line drops the garbage column (<c>:2400-2404</c>).</para>
	/// </remarks>
	[SharpFunction(Name = "lstats", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public async ValueTask<CallState> LStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? "";

		AnySharpObject? who = null;
		if (name.Length > 0 && !name.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			if (name.Equals("me", StringComparison.OrdinalIgnoreCase))
			{
				who = executor;
			}
			else if (await LocateService.LocatePlayer(parser, executor, executor, name) is AnySharpObject located
							 && located is SharpPlayer)
			{
				who = located;
			}
			else
			{
				return new CallState(ErrorMessages.Returns.NotVisible);
			}
		}

		if (who is { } subject
				&& !await ObjectStatsHelpers.CanSearchAll(executor)
				&& !await PermissionService.Controls(executor, subject))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var counts = await ObjectStatsHelpers.CountAsync(Mediator, who?.Object().DBRef);
		return new CallState(who is null ? counts.ForEveryone() : counts.ForOnePlayer());
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
		var obj = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;
		var newName = parser.CurrentState.Arguments.GetValueOrDefault("1");

		if (newName is null)
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, obj, LocateFlags.All,
				found => found.Object().Name);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found => await ObjectNameService.SetName(executor, found, newName.Message, true));
	}

	[SharpFunction(Name = "moniker", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Moniker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var obj = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;

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
		var obj1Arg = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;
		var obj2Arg = parser.CurrentState.Arguments["1"].Message.ToPlainText()!;

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
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();

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
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All, found => found.TypeString());
	}

	/// <remarks>PennMUSH's <c>fun_colors</c> (<c>src/markup.c</c>), over <see cref="PennColors"/>.</remarks>
	[SharpFunction(Name = "colors", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["ansi-string", "strip"])]
	public ValueTask<CallState> Colors(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		if (ColorConfiguration?.CurrentValue is not { Colors.Length: > 0 } colorsConfig)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		var palette = PennColors.For(colorsConfig);
		var first = args.TryGetValue("0", out var firstArg) ? firstArg.Message.ToPlainText() : string.Empty;

		if (!args.TryGetValue("1", out var formatArg))
		{
			// With a pattern every row may match, xterm ones too; without, only the named colours.
			var names = first.Length == 0
				? palette.Named.Select(c => c.Name)
				: palette.Colors.Select(c => c.Name).Where(name => MushText.IsWildcardMatch(MarkupText.Plain(name), first));
			return ValueTask.FromResult(new CallState(string.Join(" ", names)));
		}

		return ValueTask.FromResult(new CallState(PennColors.Define(first, palette) switch
		{
			PennColors.Definition definition => ColorInformation(definition, formatArg.Message.ToPlainText(), palette),
			Error<string> error => error.Value
		}));
	}

	private enum ColorStyle { Hex, Ansi16, Xterm256, Xterm256Hex, Rgb, Name, Auto }

	/// <summary><c>fun_colors</c> with two arguments: one colour code described in the formats asked for.</summary>
	private static string ColorInformation(PennColors.Definition definition, string formats, PennColors.Palette palette)
	{
		if (definition.Foreground is "" or "n" or "d" && definition.Background is "" or "n" or "D")
		{
			return ErrorMessages.Returns.ColorsRequiresAColor;
		}

		var style = ColorStyle.Hex;
		var withStyles = false;
		foreach (var word in formats.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			switch (word)
			{
				case "hex" or "x": style = ColorStyle.Hex; break;
				case "16color" or "c": style = ColorStyle.Ansi16; break;
				case "256color" or "xterm256" or "d": style = ColorStyle.Xterm256; break;
				case "xterm256x" or "h": style = ColorStyle.Xterm256Hex; break;
				case "rgb" or "r": style = ColorStyle.Rgb; break;
				case "name": style = ColorStyle.Name; break;
				case "auto": style = ColorStyle.Auto; break;
				case "styles": withStyles = true; break;
				default: return ErrorMessages.Returns.InvalidArgument;
			}
		}

		var hilite = definition.Bits.HasFlag(PennColors.Styles.Hilite);
		var result = new StringBuilder();
		if (withStyles)
		{
			// A foreground carries its own h below.
			if (definition.Foreground.Length == 0 && hilite) result.Append('h');
			if (definition.Bits.HasFlag(PennColors.Styles.Underscore)) result.Append('u');
			if (definition.Bits.HasFlag(PennColors.Styles.Flash)) result.Append('f');
			if (definition.Bits.HasFlag(PennColors.Styles.Invert)) result.Append('i');
		}

		foreach (var (color, background) in new[] { (definition.Foreground, false), (definition.Background, true) })
		{
			if (color.Length == 0)
			{
				continue;
			}

			if (background && style is not (ColorStyle.Ansi16 or ColorStyle.Auto))
			{
				result.Append('/');
			}

			var lit = !background && hilite;
			switch (style)
			{
				case ColorStyle.Auto:
					if (background && definition.Foreground.Length > 0)
					{
						result.Append(definition.Foreground.Length > 1 && color.Length == 1 ? " " : color.Length > 1 ? "/" : "");
					}

					result.Append(color);
					if (!background && color.Length == 1 && hilite) result.Append('h');
					break;
				case ColorStyle.Hex:
					result.Append(CultureInfo.InvariantCulture, $"#{PennColors.ColorToHex(color, lit, palette):x6}");
					break;
				case ColorStyle.Rgb:
					var rgb = PennColors.ColorToHex(color, lit, palette);
					result.Append(CultureInfo.InvariantCulture, $"{(rgb >> 16) & 0xFF} {(rgb >> 8) & 0xFF} {rgb & 0xFF}");
					break;
				case ColorStyle.Ansi16:
					var (letter, mapped) = PennColors.Map16(color, background, palette);
					result.Append(letter);
					if (!background && (mapped || hilite)) result.Append('h');
					break;
				case ColorStyle.Xterm256:
					result.Append(PennColors.Map256(color, lit, palette));
					break;
				case ColorStyle.Xterm256Hex:
					result.Append(PennColors.Map256(color, lit, palette).ToString("x", CultureInfo.InvariantCulture));
					break;
				case ColorStyle.Name:
					var hex = PennColors.ColorToHex(color, false, palette);
					var names = palette.Named.Where(c => c.Rgb == hex).Select(c => c.Name).ToArray();
					result.Append(names.Length > 0 ? string.Join(" ", names) : ErrorMessages.Returns.NoMatchingColorName);
					break;
			}
		}

		return result.ToString();
	}

	[SharpFunction(Name = "motd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> Motd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.ConnectMotd ?? string.Empty);
	}

	[SharpFunction(Name = "wizmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, Restrict = [PortalPermission.ChatAdmin], ParameterNames = [])]
	public async ValueTask<CallState> WizMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.WizardMotd ?? string.Empty);
	}

	[SharpFunction(Name = "downmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, Restrict = [PortalPermission.ChatAdmin], ParameterNames = [])]
	public async ValueTask<CallState> DownMotd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		return new CallState(motdData?.DownMotd ?? string.Empty);
	}

	[SharpFunction(Name = "fullmotd", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, Restrict = [PortalPermission.ChatAdmin], ParameterNames = [])]
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

		if (!args.TryGetValue("0", out var optionArg) || string.IsNullOrWhiteSpace(optionArg.Message.ToPlainText()))
		{
			var optionNames = ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Values
				.Select(name => name.ToLowerInvariant())
				.Order();
			return ValueTask.FromResult<CallState>(string.Join(" ", optionNames));
		}

		var searchTerm = optionArg.Message.ToPlainText();

		var matchingProperty = ConfigGenerated.ConfigMetadata.PropertyToAttributeName
			.FirstOrDefault(kvp => kvp.Value.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingProperty.Key != null)
		{
			var value = ConfigGenerated.ConfigAccessor.GetValue(Configuration.CurrentValue, matchingProperty.Key);
			var metadata = ConfigGenerated.ConfigMetadata.PropertyMetadata[matchingProperty.Key];
			return ValueTask.FromResult<CallState>(ConfigValueDisplay.Format(value, metadata));
		}

		return ValueTask.FromResult<CallState>(ErrorMessages.Returns.NoSuchOption);
	}

	[SharpFunction(Name = "benchmark", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
	public async ValueTask<CallState> Benchmark(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		var code = args["0"].Message;

		// fun_benchmark reads the count with is_number/parse_number, truncates it, and refuses one below
		// 1 with e_uint (src/funmisc.c:1492-1501).
		if (!ArgHelpers.TryNumber(parser, args["1"].Message.ToPlainText(), out var count)
			|| !(count >= 1 && count <= int.MaxValue))
		{
			return new CallState(ErrorMessages.Returns.UInteger);
		}

		var iterations = (int)count;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// <sendto> is evaluated and matched like any object, and must be one the executor may @pemit
		// (funmisc.c:1503-1520), before <expression> runs at all.
		AnySharpObject? sendTo = null;
		if (args.TryGetValue("2", out var sendToArg))
		{
			var name = (await ParseRawArgument(parser, sendToArg.Message))?.Message?.ToPlainText() ?? string.Empty;
			switch (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All))
			{
				case Error<CallState> error:
					return error.Value;
				case AnySharpObject found when !await CommunicationService.MayPemitAsync(parser, executor, found):
					return new CallState("#-1");
				case AnySharpObject found:
					sendTo = found;
					break;
			}
		}

		var hadErrors = false;
		CallState? last = null;
		var min = long.MaxValue;
		var max = 0L;
		var total = 0L;
		for (var i = 0; i < iterations; i++)
		{
			var start = System.Diagnostics.Stopwatch.GetTimestamp();
			last = await ParseRawArgument(parser, code);
			var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
			hadErrors |= last?.HadErrors == true;
			min = Math.Min(min, elapsed);
			max = Math.Max(max, elapsed);
			total += elapsed;
		}

		var timings = string.Create(CultureInfo.InvariantCulture,
			$"Average: {(double)total / iterations:F2}   Min: {min}   Max: {max}");

		if (sendTo is null)
		{
			return new CallState(timings) { HadErrors = hadErrors };
		}

		await NotifyService.Notify(sendTo, timings, executor);
		return (last ?? CallState.Empty) with { HadErrors = hadErrors };
	}

	[SharpFunction(Name = "fn", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public async ValueTask<CallState> Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var functionName = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		if (string.IsNullOrWhiteSpace(functionName))
		{
			return new CallState(ErrorMessages.Returns.NoFunctionNameGiven);
		}

		if (FnTarget(parser, functionName) is (var targetFunction, var deleted))
		{
			EvaluationRestrictions.Demand(targetFunction, parser.CurrentState.Restrictions);
			using var retainedSource = RestrictedTextRetention.Enter(parser.CurrentState);
			if (retainedSource is not null)
			{
				// Reserve before ToPlainText/Join allocate; the recursive parse keeps
				// this source live until it returns, including chains of fn targets.
				retainedSource.Add(functionName.Length + 2L);
				var first = true;
				foreach (var argument in parser.CurrentState.ArgumentsOrdered.Skip(1))
				{
					retainedSource.Add(argument.Value.Message.Length + (first ? 0L : 1L));
					first = false;
				}
			}
			// Build function call string and re-parse: fn(add,1,2) -> add(1,2)
			var fnArgs = parser.CurrentState.ArgumentsOrdered
				.Skip(1)
				.Select(x => x.Value.Message.ToPlainText());
			var callString = $"{functionName}({string.Join(",", fnArgs)})";
			var calling = deleted ? parser.Push(parser.CurrentState with { BuiltinCall = targetFunction }) : parser;
			var result = await calling.FunctionParse(MarkupText.Plain(callString));
			return result ?? CallState.Empty;
		}

		// Fall back to user-defined attribute function only when object-data access is allowed.
		EvaluationRestrictions.DemandObjectDataAccess(parser.CurrentState.Restrictions);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var result2 = await AttributeService.EvaluateAttributeFunctionResultAsync(
			parser,
			executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);

		// If attribute lookup returned nothing, report function not found
		if (result2.Message.ToPlainText().Length == 0)
		{
			return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, functionName.ToUpper())) { HadErrors = result2.HadErrors };
		}

		return result2;
	}

	/// <summary>
	/// The built-in fn() calls, and whether @function/delete has taken it out of the function table. PennMUSH's
	/// fn() reaches a deleted built-in all the same (builtin_func_hash_lookup, src/funufun.c:92).
	/// </summary>
	private (FunctionDefinition Definition, bool Deleted)? FnTarget(IMUSHCodeParser parser, string functionName)
		=> parser.FunctionLibrary.TryGetValue(functionName.ToLower(), out var found)
			? (found.LibraryInformation, false)
			: Builtins.TryGetValue(functionName.ToLowerInvariant(), out var deleted)
				? (deleted, true)
				: null;

	/// <summary>
	/// PennMUSH's <c>functions([&lt;type&gt;])</c> (<c>src/function.c</c> <c>list_functions</c>): the type is
	/// "builtin", "local" (@functions) or "all", and anything else is <c>#-1 INVALID ARGUMENT</c>.
	/// </summary>
	[SharpFunction(Name = "functions", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> FFunctions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var type = parser.CurrentState.Arguments.TryGetValue("0", out var arg0)
			? arg0.Message.ToPlainText()
			: string.Empty;

		return ValueTask.FromResult(type is "" or "all" or "builtin" or "local"
			? new CallState(string.Join(' ', FunctionNames(parser, type)))
			: new CallState(ErrorMessages.Returns.InvalidArgument));
	}

	/// <summary>Function names of one type — "builtin", "local", or anything else for both — sorted, upper-case.</summary>
	private static IEnumerable<string> FunctionNames(IMUSHCodeParser parser, string type)
		=> (type switch
		{
			"builtin" => parser.FunctionLibrary.AsEnumerable().Where(kv => kv.Value.IsSystem),
			"local" => parser.FunctionLibrary.AsEnumerable().Where(kv => !kv.Value.IsSystem),
			_ => parser.FunctionLibrary.AsEnumerable()
		})
			.Select(kv => kv.Value.LibraryInformation.Attribute.Name)
			.Select(name => name.ToUpperInvariant())
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal);

	/// <summary>
	/// PennMUSH's <c>fun_list</c> (<c>src/funmisc.c:1288-1327</c>). The motds are named exactly; every
	/// other option answers to any prefix of its name, tried in Penn's order, so <c>list(f)</c> is
	/// functions. A type other than builtin, local or all, an empty option and an unknown one are
	/// <c>#-1</c>. Names come back upper-case, except flags and powers, which keep their own casing as
	/// <c>@list flags</c> shows them.
	/// </summary>
	/// <remarks>
	/// Under rhost_list it is RhostMUSH's list() instead (TinyMUX's three-argument form plus a header
	/// and a target), iter() that tells each result in turn and returns nothing, which is why the
	/// arguments are not evaluated before the call.
	/// </remarks>
	[SharpFunction(Name = "list", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.NoParse)]
	public async ValueTask<CallState> List(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (Compatibility(parser).RhostList)
		{
			return await TellEachAsync(parser);
		}

		var args = parser.CurrentState.Arguments;
		if (args.Count > 2)
		{
			return new CallState(ErrorMessages.Returns.ArgumentCount("LIST", 1, 2, args.Count));
		}

		var errors = new ListEvaluationErrors();
		var option = args.TryGetValue("0", out var a0) ? errors.Record(await a0.GetParsedResultAsync()).ToPlainText() : string.Empty;
		var type = "all";
		if (args.TryGetValue("1", out var a1))
		{
			type = errors.Record(await a1.GetParsedResultAsync()).ToPlainText().ToLowerInvariant();
			if (type is not ("builtin" or "local" or "all"))
				return errors.Complete(new CallState(ErrorMessages.Returns.Nothing));
		}

		bool Names(string full) => full.StartsWith(option, StringComparison.OrdinalIgnoreCase);

		return errors.Complete(option switch
		{
			"" => new CallState(ErrorMessages.Returns.Nothing),
			_ when option.Equals("motd", StringComparison.OrdinalIgnoreCase) => await Motd(parser, default!),
			_ when option.ToLowerInvariant() is "wizmotd" or "downmotd" or "fullmotd"
				=> await GetWizardMotdAsync(parser, option.ToLowerInvariant()),
			_ when Names("functions") => new CallState(string.Join(' ', FunctionNames(parser, type))),
			_ when Names("@functions") => new CallState(string.Join(' ', FunctionNames(parser, "local"))),
			_ when Names("commands") => new CallState(string.Join(' ', CommandNames(parser, type))),
			_ when Names("attribs") => await UpperSorted(Mediator.CreateStream(new GetAllAttributeEntriesQuery()).Select(x => x.Name)),
			_ when Names("locks") => await UpperSorted(Enum.GetNames<LockType>().ToAsyncEnumerable()),
			_ when Names("flags") => new CallState(await FlagListAsync(parser,
				Mediator.CreateStream(new GetAllObjectFlagsQuery()).Select(f => (f.Name, f.Symbol, f.SetPermissions, f.Disabled)))),
			_ when Names("powers") => new CallState(await FlagListAsync(parser,
				Mediator.CreateStream(new GetPowersQuery()).Select(p => (p.Name, p.Symbol, p.SetPermissions, p.Disabled)))),
			_ => new CallState(ErrorMessages.Returns.Nothing)
		});

		static async ValueTask<CallState> UpperSorted(IAsyncEnumerable<string> names)
			=> new(string.Join(' ', (await names.ToArrayAsync())
				.Select(name => name.ToUpperInvariant())
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)));

		async ValueTask<CallState> GetWizardMotdAsync(IMUSHCodeParser parser, string which)
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			if (!await executor.Can(PortalPermission.ChatAdmin))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			return which switch
			{
				"wizmotd" => await WizMotd(parser, default!),
				"downmotd" => await DownMotd(parser, default!),
				_ => await FullMotd(parser, default!)
			};
		}
	}

	/// <summary>
	/// RhostMUSH's list(list, action[, delimiter][, header][, target]), whose first three arguments
	/// are TinyMUX's list(): the action is evaluated for each element, as by iter(), and each result is
	/// told to the enactor. The call itself returns nothing, and an empty list tells nothing, not even
	/// the header. A header that is not empty is told once, before the results. The header and the
	/// results go to the target instead when the enactor controls it; a target that is not found or
	/// not controlled is ignored without a word, as RhostMUSH ignores it.
	/// </summary>
	private async ValueTask<CallState> TellEachAsync(IMUSHCodeParser parser)
	{
		var args = parser.CurrentState.Arguments;
		if (args.Count < 2)
		{
			return new CallState(ErrorMessages.Returns.ArgumentCount("LIST", 2, 5, args.Count));
		}

		var errors = new ListEvaluationErrors();
		var list = errors.Record(await args["0"].GetParsedResultAsync());
		var delimiter = args.TryGetValue("2", out var a2) && a2.Message.Length > 0
			? errors.Record(await a2.GetParsedResultAsync())
			: MarkupText.Space;
		var items = MushText.SplitList(delimiter, list);
		if (items is [] or [{ Length: 0 }])
		{
			return errors.Complete(CallState.Empty);
		}

		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var iteration = new IterationWrapper<MString>
		{
			Value = MarkupText.Empty, Break = false, NoBreak = false, Iteration = 0,
			Spliced = true, Evaluation = parser.CurrentState.CurrentEvaluation
		};

		parser.CurrentState.IterationRegisters.Push(iteration);
		try
		{
			var recipient = args.TryGetValue("4", out var a4) && a4.Message.Length > 0
				? await ListRecipientAsync(parser, enactor, executor, errors.Record(await a4.GetParsedResultAsync()))
				: enactor;
			if (args.TryGetValue("3", out var a3) && a3.Message.Length > 0)
			{
				await NotifyService.Notify(recipient, errors.Record(await a3.GetParsedResultAsync()), executor);
			}

			foreach (var item in items)
			{
				iteration.Value = item;
				iteration.Iteration++;
				await NotifyService.Notify(recipient, errors.Record(await args["1"].GetParsedResultAsync()), executor);
				if (iteration.Break)
				{
					break;
				}
			}
		}
		finally
		{
			parser.CurrentState.IterationRegisters.TryPop(out _);
		}

		return errors.Complete(CallState.Empty);
	}

	/// <summary>
	/// RhostMUSH's list() target: the name is matched as the executor would match it, and the match
	/// is used only when the enactor controls it. Anything else leaves the enactor.
	/// </summary>
	private async ValueTask<AnySharpObject> ListRecipientAsync(IMUSHCodeParser parser, AnySharpObject enactor,
		AnySharpObject executor, MString name)
		=> await LocateService.Locate(parser, executor, executor, name.ToPlainText(), LocateFlags.All) is AnySharpObject target
			&& await PermissionService.Controls(enactor, target)
				? target
				: enactor;

	/// <summary>Command names of one type — "builtin", "local", or anything else for both — sorted, upper-case.</summary>
	private static IEnumerable<string> CommandNames(IMUSHCodeParser parser, string type)
		=> (type switch
		{
			"builtin" => parser.CommandLibrary.AsEnumerable().Where(kv => kv.Value.IsSystem),
			"local" => parser.CommandLibrary.AsEnumerable().Where(kv => !kv.Value.IsSystem),
			_ => parser.CommandLibrary.AsEnumerable()
		})
			.Select(kv => kv.Value.LibraryInformation.Attribute.Name.ToUpperInvariant())
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal);

	/// <summary>Penn's <c>list_all_flags</c> with <c>FLAG_LIST_NAMECHAR</c>, as <c>@list flags</c> prints it.</summary>
	private async ValueTask<string> FlagListAsync(IMUSHCodeParser parser,
		IAsyncEnumerable<(string Name, string Symbol, string[] SetPermissions, bool Disabled)> flags)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return FlagListHelpers.Format(await flags.ToArrayAsync(), executor.IsGod(), await executor.IsPriv());
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
			command = args["0"].Message;
			switches = "all";
		}
		else
		{
			// scan(<looker>, <command>[, <switches>])
			var lookerName = args["0"].Message.ToPlainText();
			command = args["1"].Message;
			switches = args.ContainsKey("2") ? args["2"].Message.ToPlainText() : "all";

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

		if (checkInventory && looker.AsOptionalContainer is AnySharpContainer inventory)
		{
			await AddContents(inventory);
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
			// The configured master room, where command dispatch looks for global $-commands; it is not #0.
			if (await Mediator.Send(new GetObjectNodeQuery(new DBRef(Convert.ToInt32(Configuration.CurrentValue.Database.MasterRoom))))
					is AnySharpObject masterRoom)
			{
				objectsToScan.Add(masterRoom);

				if (masterRoom.AsOptionalContainer is AnySharpContainer masterContainer)
				{
					await AddContents(masterContainer);
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
			command,
			looker);

		if (!matchResult.TryGetValue(out var matches))
		{
			return CallState.Empty;
		}

		// Format results as "dbref/attribute" pairs
		var results = matches.Select(match =>
			$"{match.SObject.Object().DBRef}/{match.Attribute.LongName}");

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
		var category = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var str = parser.CurrentState.Arguments["1"].Message;
		var target = parser.CurrentState.Arguments.Count >= 3
			? parser.CurrentState.Arguments["2"].Message.ToPlainText()
			: null;
		// valid() runs as the executor: its optional <target> is located, and the default validation
		// context, relative to the executor (PennMUSH matches function arguments to the executor) — not
		// the caller one frame up the u() chain.
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// A SharpMUSH category: whether roll() can read a formula, whatever its names turn out to hold.
		if (category == "roll")
		{
			return new CallState(DiceParser.IsValid(str.ToPlainText()) ? "1" : "0");
		}

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
			"rolename" => IValidateService.ValidationType.RoleName,
			"rolecategory" => IValidateService.ValidationType.RoleCategory,
			"permission" => IValidateService.ValidationType.Permission,
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

	/// <summary>
	/// RhostMUSH's <c>error([object])</c>: the message a command that matches nothing gets. RhostMUSH may draw
	/// it from a file of its own, and the object picks whose; SharpMUSH has the one message.
	/// </summary>
	[SharpFunction(Name = "error", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public ValueTask<CallState> UnmatchedCommandMessage(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(ErrorMessages.Notifications.HuhTypeHelp);

	[SharpFunction(Name = "poll", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> Poll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var pollData = await ObjectDataService.GetExpandedServerDataAsync<PollData>();
		return new CallState(pollData?.Message ?? string.Empty);
	}
}
