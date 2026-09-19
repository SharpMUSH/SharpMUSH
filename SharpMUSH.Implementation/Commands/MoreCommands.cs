using System.Buffers;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@CLOCK", Switches = ["JOIN", "SPEAK", "MOD", "SEE", "HIDE"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 1, MaxArgs = 2, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ChannelLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		var channelName = args["0"].Message!;
		var lockKey = args.TryGetValue("1", out var arg1) ? arg1.Message!.ToPlainText() : string.Empty;

		var lockType = switches.FirstOrDefault() ?? "JOIN";
		lockType = lockType.ToUpper();

		// Setting a lock on a channel you cannot see must be refused the same way as setting one on a
		// channel that does not exist, or @clock reports which names are taken. notify: true because the
		// gate emits ONE refusal for both cases: suppressing it does not make the two cases more alike, it
		// only makes a mistyped channel name fail in silence.
		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, notify: true) switch
		{
			SharpChannel channel => await SetChannelLockAsync(executor, channel, lockType, lockKey),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> SetChannelLockAsync(AnySharpObject executor, SharpChannel channel,
		string lockType, string lockKey)
	{
		// An absent modify lock grants no additional rights beyond the owner and wizard gates.
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (lockType is not ("JOIN" or "SPEAK" or "SEE" or "HIDE" or "MOD"))
		{
			await NotifyService.Notify(executor, $"Invalid lock type: {lockType}", executor);
			return new CallState(ErrorMessages.Returns.InvalidLockType);
		}

		if (!string.IsNullOrEmpty(lockKey))
		{
			if (await BooleanExpressionParser.BindAsync(lockKey, executor, ExecutionBudget.CurrentToken) is not string bound)
			{
				await NotifyService.Notify(executor, "CHAT: I don't understand that key.", executor);
				return new CallState(ErrorMessages.Returns.InvalidLock);
			}
			lockKey = bound;
		}

		UpdateChannelCommand updateCommand = lockType switch
		{
			"JOIN" => new UpdateChannelCommand(channel, null, null, null, lockKey, null, null, null, null, null, null),
			"SPEAK" => new UpdateChannelCommand(channel, null, null, null, null, lockKey, null, null, null, null, null),
			"SEE" => new UpdateChannelCommand(channel, null, null, null, null, null, lockKey, null, null, null, null),
			"HIDE" => new UpdateChannelCommand(channel, null, null, null, null, null, null, lockKey, null, null, null),
			"MOD" => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, lockKey, null, null),
			_ => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, null, null, null)
		};

		await Mediator.Send(updateCommand, ExecutionBudget.CurrentToken);

		if (string.IsNullOrEmpty(lockKey))
		{
			await NotifyService.Notify(executor, $"{lockType} lock removed from channel {channel.Name.ToPlainText()}.", executor);
		}
		else
		{
			await NotifyService.Notify(executor, $"{lockType} lock set on channel {channel.Name.ToPlainText()}.", executor);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@LOGWIPE", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "ROTATE", "TRIM", "WIPE"],
		Behavior = CB.Default | CB.NoGagged | CB.God, MinArgs = 0, MaxArgs = 0, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> LogWipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (!executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var logTypes = new[] { "CMD", "CONN", "ERR", "TRACE", "WIZ" };
		var actions = new[] { "ROTATE", "TRIM", "WIPE", "CHECK" };

		var specifiedLogType = switches.FirstOrDefault(s => logTypes.Contains(s));
		var specifiedAction = switches.FirstOrDefault(s => actions.Contains(s)) ?? "CHECK";

		if (specifiedLogType == null && specifiedAction == "CHECK")
		{
			await NotifyService.Notify(executor, "Log Management Status:", executor);
			await NotifyService.Notify(executor, "  SharpMUSH uses .NET logging infrastructure", executor);
			await NotifyService.Notify(executor, "  Logs are managed by configured logging providers", executor);
			await NotifyService.Notify(executor, "  Available log types: CMD, CONN, ERR, TRACE, WIZ", executor);
			await NotifyService.Notify(executor, "  Available actions: ROTATE, TRIM, WIPE", executor);
			await NotifyService.Notify(executor, "  Note: Direct log file manipulation not yet implemented", executor);
			Logger?.LogInformation("@LOGWIPE/CHECK executed by {Executor}", executor.Object().Name);
		}
		else
		{
			var logDesc = specifiedLogType ?? "all logs";
			await NotifyService.Notify(executor, $"@LOGWIPE/{specifiedAction}: Would {specifiedAction.ToLower()} {logDesc}", executor);
			await NotifyService.Notify(executor, "Direct log file manipulation not yet implemented.", executor);
			await NotifyService.Notify(executor, "Configure log rotation through appsettings.json or hosting provider.", executor);
			Logger?.LogWarning("@LOGWIPE/{Action} requested for {LogType} by {Executor} - not implemented",
				specifiedAction, logDesc, executor.Object().Name);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object/lock", "flags"])]
	public async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute attribute)
	{
		if (await RejectIfTooFewArguments(parser, attribute) is { } error) return error;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var slash = target.IndexOf('/');
		if (slash < 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoLockNameGiven), executor);
			return CallState.Empty;
		}
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, target[..slash], LocateFlags.All,
			async obj =>
			{
				var flags = args["1"].Message!.ToPlainText();
				var result = await LockService.SetFlagsAsync(executor, obj, target[(slash + 1)..], flags);
				if (result is Error<string> failure) await NotifyService.Notify(executor, failure.Value, executor);
				else if (!await obj.Object().AreQuietAsync(executor))
					await NotifyService.NotifyLocalized(executor, flags.StartsWith('!')
						? nameof(ErrorMessages.Notifications.LockFlagsUnset)
						: nameof(ErrorMessages.Notifications.LockFlagsSet), executor,
						obj.Object().Name, LockNames.Display(target[(slash + 1)..]));
				return CallState.Empty;
			});
	}

	[SharpCommand(Name = "@MALIAS",
		Switches =
		[
			"SET", "CREATE", "DESTROY", "DESCRIBE", "RENAME", "STATS", "CHOWN", "NUKE", "ADD", "REMOVE", "LIST", "ALL", "WHO",
			"MEMBERS", "USEFLAG", "SEEFLAG"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["alias", "list"])]
	public async ValueTask<Option<CallState>> MailAlias(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		var action = switches.FirstOrDefault() ?? "LIST";

		await NotifyService.Notify(executor, $"@MALIAS/{action}: Mail alias system not yet implemented.", executor);
		await NotifyService.Notify(executor, "This command would manage mail distribution lists and aliases.", executor);

		return CallState.Empty;
	}

	/// <summary>
	/// <c>@sockset [&lt;descriptor&gt;]=&lt;option&gt;,&lt;value&gt;[,&lt;option&gt;,&lt;value&gt;…]</c> —
	/// PennMUSH <c>cmd_sockset</c> (src/cmds.c). The in-game face of the same option engine the
	/// <c>SOCKSET</c> socket command drives, with a descriptor argument so a wizard can adjust someone
	/// else's connection.
	///
	/// <para>
	/// Not wizard-only: PennMUSH lets anyone read and set options on their <i>own</i> descriptor here,
	/// and only requires privilege to reach another player's. Refusing mortals outright, as this
	/// command used to, made <c>@sockset</c> useless for the people it is mostly for.
	/// </para>
	/// </summary>
	[SharpCommand(Name = "@SOCKSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["socket", "option", "value"])]
	public async ValueTask<Option<CallState>> SocketSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var isWizard = await executor.IsWizard();

		var descriptorArg = args.TryGetValue("0", out var arg0) ? arg0.Message?.ToPlainText().Trim() ?? string.Empty : string.Empty;

		var target = await ResolveSocksetTarget(parser, executor, descriptorArg, isWizard);
		if (target is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SocksetInvalidDescriptor), executor);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		// PennMUSH compares *player* identity here, not descriptor identity: a player with two clients
		// open may @sockset either of their own connections. Only reaching someone else's needs wizard.
		var isOwnDescriptor = target.Ref == executor.Object().DBRef;

		if (!isOwnDescriptor && !isWizard)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// PennMUSH walks args_right in (option, value) pairs starting at index 1, so the right-hand
		// side is "OPTION,VALUE" — not "OPTION=VALUE" — and several pairs may be set in one command.
		var pairs = args.Where(kv => kv.Key != "0")
			.OrderBy(kv => int.Parse(kv.Key))
			.Select(kv => kv.Value.Message?.ToPlainText() ?? string.Empty)
			.ToArray();

		if (pairs.Length == 0)
		{
			await NotifyService.Notify(executor, SocketOptions.Show(target, "\n",
				await ArgHelpers.ColorFlagsOfAsync(Mediator, target.Ref)), executor);
			return CallState.Empty;
		}

		for (var i = 0; i + 1 < pairs.Length; i += 2)
		{
			var result = SocketOptions.Set(target, pairs[i], pairs[i + 1]);
			await NotifyService.NotifyLocalized(executor, result.Key, result.Arguments);
		}

		// Once, after the whole run: several pairs may be set in one command, and only the descriptor's
		// final state is worth telling the socket owner about.
		await PublishColorStyleAsync(target);

		// An odd trailing element means the last option arrived without a value; PennMUSH answers the
		// same way it answers an empty option name.
		if (pairs.Length % 2 != 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SocksetSetWhatOption), executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH <c>lookup_desc()</c> (src/bsd.c): an empty argument means "the descriptor I am on", a
	/// number means that descriptor, and anything else is a player name whose least-idle connection is
	/// used.
	///
	/// <para>
	/// A descriptor number resolves for an unprivileged executor only when the descriptor is theirs.
	/// PennMUSH returns NULL otherwise, so the caller reports "Invalid descriptor." rather than a
	/// permission error: refusing by permission would tell a mortal which handle numbers are live.
	/// </para>
	/// </summary>
	private async ValueTask<IConnectionService.ConnectionData?> ResolveSocksetTarget(
		IMUSHCodeParser parser, AnySharpObject executor, string descriptorArg, bool isWizard)
	{
		if (descriptorArg.Length == 0)
		{
			return CurrentConnection(parser) ?? await LeastIdleConnection(executor.Object().DBRef);
		}

		if (long.TryParse(descriptorArg, out var handle))
		{
			var connection = ConnectionService.Get(handle);

			return connection is not null && (isWizard || connection.Ref == executor.Object().DBRef)
				? connection
				: null;
		}

		var player = await Mediator.CreateStream(new GetPlayerQuery(descriptorArg)).FirstOrDefaultAsync();
		if (player is null) return null;

		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);

		return isWizard || playerRef == executor.Object().DBRef
			? await LeastIdleConnection(playerRef)
			: null;
	}

	/// <inheritdoc cref="ArgHelpers.LeastIdleConnectionAsync"/>
	private ValueTask<IConnectionService.ConnectionData?> LeastIdleConnection(DBRef who)
		=> ArgHelpers.LeastIdleConnectionAsync(ConnectionService, who);

	[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Slave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.Notify(executor, "Slave command does nothing for SharpMUSH.", executor);
		return new None();
	}

	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await NotifyService.Notify(executor, "@UNRECYCLE: Object recovery system not yet implemented.", executor);
		await NotifyService.Notify(executor, "This command would restore objects from the recycle bin.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@WARNINGS", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Warnings(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.TryGetValue("0", out var objectArg) || string.IsNullOrWhiteSpace(objectArg.Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Usage: @warnings <object>=<warning list>", executor);
			await NotifyService.Notify(executor, "Available warnings: none, serious, normal, extra, all", executor);
			await NotifyService.Notify(executor, "Individual: exit-unlinked, exit-oneway, exit-multiple, exit-msgs, exit-desc,", executor);
			await NotifyService.Notify(executor, "           thing-msgs, thing-desc, room-desc, my-desc, lock-checks", executor);
			await NotifyService.Notify(executor, "Use !warning to negate (e.g., 'all !exit-desc')", executor);
			return CallState.Empty;
		}

		if (!args.TryGetValue("1", out var warningListArg))
		{
			await NotifyService.Notify(executor, "Usage: @warnings <object>=<warning list>", executor);
			return CallState.Empty;
		}

		var objectString = objectArg.Message?.ToString() ?? string.Empty;
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectString,
			LocateFlags.All) switch
		{
			AnySharpObject target => await SetWarningsAsync(executor, target, warningListArg),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> SetWarningsAsync(AnySharpObject executor, AnySharpObject target,
		CallState warningListArg)
	{
		var targetObj = target.Object();

		if (!await PermissionService.Controls(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var warningListString = warningListArg.Message?.ToPlainText() ?? string.Empty;
		var unknownWarnings = new List<string>();
		var newWarnings = WarningTypeHelper.ParseWarnings(warningListString, unknownWarnings);

		foreach (var unknown in unknownWarnings)
		{
			await NotifyService.Notify(executor, $"Unknown warning: {unknown}", executor);
		}

		var oldWarnings = targetObj.Warnings;

		await Mediator.Send(new SetObjectWarningsCommand(target, newWarnings));

		if (newWarnings != WarningType.None)
		{
			var warningString = WarningTypeHelper.UnparseWarnings(newWarnings);
			await NotifyService.Notify(executor, $"Warnings set to: {warningString}", executor);
		}
		else
		{
			await NotifyService.Notify(executor, "Warnings cleared.", executor);
		}

		Logger?.LogInformation("@WARNINGS: {Executor} set warnings on {Target} from {Old} to {New}",
			executor.Object().Name, targetObj.Name, oldWarnings, newWarnings);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@WCHECK", Switches = ["ALL", "ME"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> WizardCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		var checkAll = switches.Contains("ALL");
		var checkMe = switches.Contains("ME");

		if (checkAll)
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.Notify(executor, "You'd better check your wizbit first.", executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await NotifyService.Notify(executor, "Running database topology warning checks...", executor);
			var checkedCount = await WarningService.CheckAllObjectsAsync();
			await NotifyService.Notify(executor, $"Warning checks complete. Checked {checkedCount} objects.", executor);

			Logger?.LogInformation("@WCHECK/ALL executed by {Executor}, checked {Count} objects",
				executor.Object().Name, checkedCount);
		}
		else if (checkMe)
		{
			await NotifyService.Notify(executor, "Checking objects you own...", executor);
			var warningCount = await WarningService.CheckOwnedObjectsAsync(executor);

			Logger?.LogInformation("@WCHECK/ME executed by {Executor}, found {Count} warnings",
				executor.Object().Name, warningCount);
		}
		else
		{
			if (!args.TryGetValue("0", out var objectArg) || string.IsNullOrWhiteSpace(objectArg.Message?.ToPlainText()))
			{
				await NotifyService.Notify(executor, "Usage: @wcheck <object> or @wcheck/me or @wcheck/all", executor);
				return CallState.Empty;
			}

			var objectString = objectArg.Message?.ToString() ?? string.Empty;
			return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectString,
				LocateFlags.All) switch
			{
				AnySharpObject target => await WarningCheckObjectAsync(executor, target),
				Error<CallState> error => error.Value
			};
		}

		return CallState.Empty;
	}

	/// <summary>Runs the warning checks on one object its owner, or a See_All viewer, names.</summary>
	private async ValueTask<Option<CallState>> WarningCheckObjectAsync(AnySharpObject executor, AnySharpObject target)
	{
		var targetObj = target.Object();
		var targetOwner = await targetObj.Owner.WithCancellation(CancellationToken.None);

		if (!(await executor.IsSee_All() || targetOwner.Object.DBRef.Equals(executor.Object().DBRef)))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await WarningService.CheckObjectAsync(executor, target);
		await NotifyService.Notify(executor, "@wcheck complete.", executor);

		Logger?.LogInformation("@WCHECK executed by {Executor} on {Target}",
			executor.Object().Name, targetObj.Name);

		return CallState.Empty;
	}

	[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Brief(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		AnyOptionalSharpObject viewing;

		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToPlainText();

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				argText,
				LocateFlags.All);

			if (locate is not AnySharpObject located)
			{
				return new None();
			}

			viewing = located;
		}
		else
		{
			viewing = (await Mediator.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing is not AnySharpObject viewingKnown)
		{
			return new None();
		}

		var canExamine = await PermissionService.CanExamine(executor, viewingKnown);

		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService.Notify(enactor, $"{limitedObj.Name} is owned by {limitedOwnerObj.Name}.", enactor);
			return new CallState(limitedObj.DBRef.ToString());
		}

		var perceive = await ObserveRealityAsync(parser, executor);
		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator.CreateStream(new GetContentsQuery(viewingKnown.AsContainer), ExecutionBudget.CurrentToken)
				.Where((item, ct) => perceive(item.Object().DBRef, ct))
				.ToArrayAsync(ExecutionBudget.CurrentToken);

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var objPowers = obj.Powers.Value;
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);

		var outputSections = new List<MString>();

		var showFlags = Configuration.CurrentValue.Cosmetic.FlagsOnExamine;
		var nameRow = showFlags
			? MarkupText.Concat([
				name.Hilight(),
				MarkupText.Space,
				MarkupText.Plain($"(#{obj.DBRef.Number}{MessageFormatting.FlagSymbols(objFlags)})")
			])
			: MarkupText.Concat(name.Hilight(), MarkupText.Plain($" (#{obj.DBRef.Number})"));

		outputSections.Add(nameRow);

		if (showFlags)
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}"));
		}
		else
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type}"));
		}

		var ownerRow = showFlags
			? MarkupText.Plain($"Owner: {ownerName.Hilight()}" +
											 $"(#{ownerObj.DBRef.Number}{await MessageFormatting.FlagSymbolsAsync(ownerObj)})")
			: MarkupText.Plain($"Owner: {ownerName.Hilight()}(#{ownerObj.DBRef.Number})");
		outputSections.Add(ownerRow);

		outputSections.Add(MarkupText.Plain($"Parent: {objParent.Object()?.Name ?? "*NOTHING*"}"));

		foreach (var (lockName, lockData) in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
			outputSections.Add(MarkupText.Plain(await FormatLockLineAsync(executor, lockName, lockData)));

		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		if (powersList.Length > 0)
		{
			outputSections.Add(MarkupText.Plain($"Powers: {string.Join(" ", powersList)}"));
		}

		if (viewingKnown.IsPlayer || viewingKnown.IsThing)
		{
			if (await viewingKnown.MinusRoom().Home() is not AnySharpContainer homeObj)
			{
				throw new InvalidOperationException("Players and things always have a home.");
			}

			outputSections.Add(MarkupText.Plain($"Home: {homeObj.Object().Name}(#{homeObj.Object().DBRef.Number})"));

			var locationObj = await viewingKnown.Where();
			outputSections.Add(MarkupText.Plain($"Location: {locationObj.Object().Name}(#{locationObj.Object().DBRef.Number})"));
		}

		outputSections.Add(MarkupText.Plain($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F}"));

		await NotifyService.Notify(enactor, MarkupText.Join(MarkupText.Plain("\n"), outputSections), enactor);

		if (!switches.Contains("OPAQUE") && contents.Length > 0)
		{
			var contentNames = contents.Select(x => x.Object().Name);
			await NotifyService.Notify(enactor, $"Contents:", enactor);
			foreach (var contentName in contentNames)
			{
				await NotifyService.Notify(enactor, $"  {contentName}", enactor);
			}
		}

		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "PAGE", Switches = ["LIST", "NOEVAL", "PORT", "OVERRIDE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["player", "message"])]
	public async ValueTask<Option<CallState>> Page(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var isNoEval = parser.CurrentState.Switches.Contains("NOEVAL");
		var isOverride = parser.CurrentState.Switches.Contains("OVERRIDE");
		var isList = parser.CurrentState.Switches.Contains("LIST");
		if (isList)
		{
			var lastPagedAttr = await AttributeService.GetAttributeAsync(
				executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Read, false);
			var lastPagedText = lastPagedAttr is SharpAttribute[] attr
				? attr.Last().Value.ToPlainText()
				: string.Empty;

			if (string.IsNullOrWhiteSpace(lastPagedText))
			{
				await NotifyService.Notify(executor, "You haven't paged anyone since connecting.", executor);
				return CallState.Empty;
			}

			var lastPagedNames = new List<string>();
			foreach (var recipientRef in lastPagedText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (!DBRef.TryParse(recipientRef, out var dbref))
				{
					continue;
				}

				if (await Mediator.Send(new GetObjectNodeQuery(dbref!.Value)) is AnySharpObject recipient)
				{
					lastPagedNames.Add(recipient.Object().Name);
				}
			}

			if (lastPagedNames.Count == 0)
			{
				await NotifyService.Notify(executor, "I can't find who you last paged.", executor);
			}
			else
			{
				var recipientList = MessageFormatting.FormatWithOxfordComma(lastPagedNames);
				await NotifyService.Notify(executor, $"You last paged {recipientList}.", executor);
			}

			return CallState.Empty;
		}

		var recipientsArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MarkupText.Empty);

		string recipientsText;

		// If no recipients are provided, use the last successful page targets.
		if (string.IsNullOrWhiteSpace(recipientsArg.ToPlainText()) &&
			!string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			var lastPagedAttr = await AttributeService.GetAttributeAsync(
				executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Read, false);
			recipientsText = lastPagedAttr is SharpAttribute[] attr
				? attr.Last().Value.ToPlainText()
				: string.Empty;

			if (string.IsNullOrWhiteSpace(recipientsText))
			{
				await NotifyService.Notify(executor, "Who do you want to page?", executor);
				return CallState.Empty;
			}
		}
		else
		{
			recipientsText = recipientsArg.ToPlainText();
		}

		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "What do you want to page?", executor);
			return CallState.Empty;
		}

		var pageType = messageArg.ToPlainText()[0] switch
		{
			':' => PageMessageType.Pose,
			';' => PageMessageType.SemiPose,
			_ => PageMessageType.Speech
		};
		var message = pageType == PageMessageType.Speech
			? messageArg
			: messageArg.Substring(1);

		var recipientNames = recipientsText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var successfulRecipients = new List<AnySharpObject>();

		foreach (var recipientName in recipientNames)
		{
			if (await LocateService.LocateAndNotifyIfInvalidWithCallState(
					parser, executor, executor, recipientName, LocateFlags.All | LocateFlags.MatchForPage)
				is not AnySharpObject recipient)
			{
				continue;
			}

			if (!isOverride)
			{
				var recipientFlags = recipient.Object().Flags.Value;
				if (await recipientFlags.AnyAsync(f => f.Name.Equals("HAVEN", StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService.Notify(executor, $"{recipient.Object().Name} is not accepting pages.", executor);
					continue;
				}
			}

			if (!isOverride)
			{
				// The interaction filter is its own gate and carries no failure triad: `fails_lock` at
				// speech.c:924-925 is `eval_lock_with(executor, target, Page_Lock, pe_info)` alone, and
				// only it reaches the fail_lock at :948.
				if (!await PermissionService.CanInteract(executor, recipient,
							IPermissionService.InteractType.Page))
				{
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.NotAcceptingYourPages, recipient.Object().Name),
						executor);

					continue;
				}

				if (!await LockService.Evaluate(LockType.Page, recipient, executor))
				{
					// speech.c:944-948: the pager is told, and then
					// fail_lock(executor, target, Page_Lock, NULL, NOTHING). The Page lock is not in
					// lock_msgs, so its failure attributes are the derived PAGE_LOCK`FAILURE /
					// `OFAILURE / `AFAILURE (lock.c:861-870) that LockMessages.FailureAttributes
					// builds, and FailLock evaluates them as the recipient. No default: Penn passes
					// NULL.
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.NotAcceptingYourPages, recipient.Object().Name),
						executor);

					await DidItService.FailLock(parser, executor, recipient, LockType.Page);

					continue;
				}
			}

			successfulRecipients.Add(recipient);
		}

		if (successfulRecipients.Count > 0)
		{
			var recipientList = MessageFormatting.FormatWithOxfordComma(
				successfulRecipients.Select(r => r.Object().Name).ToArray());
			var recipientRefs = string.Join(" ",
				successfulRecipients.Select(r => $"#{r.Object().DBRef.Number}"));
			var pageAlias = executor is SharpPlayer executorPlayer
				? executorPlayer.Aliases?.FirstOrDefault() ?? string.Empty
				: string.Empty;
			var senderName = Configuration.CurrentValue.Cosmetic.PageAliases && !string.IsNullOrEmpty(pageAlias)
				? $"{executor.Object().Name} ({pageAlias})"
				: executor.Object().Name;
			var recipientSuffix = successfulRecipients.Count > 1 ? $" (to {recipientList})" : string.Empty;

			var incomingDefault = pageType switch
			{
				PageMessageType.Speech => MarkupText.Concat([
					MarkupText.Plain(successfulRecipients.Count > 1
						? $"{senderName} pages {recipientList}: "
						: $"{senderName} pages: "),
					message
				]),
				PageMessageType.Pose => MarkupText.Concat([
					MarkupText.Plain($"From afar{recipientSuffix}, {senderName} "),
					message
				]),
				_ => MarkupText.Concat([
					MarkupText.Plain($"From afar{recipientSuffix}, {senderName}"),
					message
				])
			};
			var outgoingDefault = pageType switch
			{
				PageMessageType.Speech => MarkupText.Concat([
					MarkupText.Plain($"You paged {recipientList} with '"),
					message,
					MarkupText.Plain("'")
				]),
				PageMessageType.Pose => MarkupText.Concat([
					MarkupText.Plain($"Long distance to {recipientList}: {executor.Object().Name} "),
					message
				]),
				_ => MarkupText.Concat([
					MarkupText.Plain($"Long distance to {recipientList}: {executor.Object().Name}"),
					message
				])
			};
			var pageTypeToken = pageType switch
			{
				PageMessageType.Pose => ":",
				PageMessageType.SemiPose => ";",
				_ => "\""
			};
			var lastPagedText = string.Join(" ", successfulRecipients.Select(r => r.Object().DBRef));
			var lastPagedResult = await AttributeService.SetAttributeAsync(
				await HelperFunctions.GetGod(Mediator), executor, "LASTPAGED", MarkupText.Plain(lastPagedText));
			if (lastPagedResult is Error<string> error)
			{
				await NotifyService.Notify(executor, error.Value, executor);
				return CallState.Empty;
			}

			var outPageFormatArgs = PageFormatArguments(
				message, pageTypeToken, pageAlias, recipientRefs, outgoingDefault);
			var outgoing = await parser.With(
				state => state with
				{
					Executor = executor.Object().DBRef,
					Caller = executor.Object().DBRef,
					Enactor = executor.Object().DBRef
				},
				pageParser => AttributeHelpers.EvaluateFormatAttribute(
					AttributeService, pageParser, executor, executor, "OUTPAGEFORMAT",
					outPageFormatArgs, outgoingDefault, checkParents: true));
			await NotifyService.Notify(executor, outgoing, executor);

			foreach (var recipient in successfulRecipients)
			{
				var pageFormatArgs = PageFormatArguments(
					message, pageTypeToken, pageAlias, recipientRefs, incomingDefault);
				var incoming = await parser.With(
					state => state with
					{
						Executor = recipient.Object().DBRef,
						Caller = recipient.Object().DBRef,
						Enactor = executor.Object().DBRef
					},
					pageParser => AttributeHelpers.EvaluateFormatAttribute(
						AttributeService, pageParser, recipient, recipient, "PAGEFORMAT",
						pageFormatArgs, incomingDefault, checkParents: true));
				await NotifyService.Notify(recipient, incoming, executor, INotifyService.NotificationType.Say);
			}
		}
		else if (recipientNames.Length > 0)
		{
			await NotifyService.Notify(executor, "No one to page.", executor);
		}

		return CallState.Empty;
	}

	private static Dictionary<string, CallState> PageFormatArguments(
		MString message, string pageType, string alias, string recipientRefs, MString defaultMessage) => new()
		{
			["0"] = new CallState(message),
			["1"] = new CallState(pageType),
			["2"] = new CallState(alias),
			["3"] = new CallState(recipientRefs),
			["4"] = new CallState(defaultMessage)
		};

	private enum PageMessageType
	{
		Speech,
		Pose,
		SemiPose
	}

	[SharpCommand(Name = "POSE", Switches = ["NOEVAL", "NOSPACE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 1, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Pose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var message = parser.CurrentState.Switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		return await CommunicationService.SpeechAsync(parser, message, parser.CurrentState.Switches.Contains("NOSPACE") ? ";" : ":");
	}

	[SharpCommand(Name = "SAY", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Say(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var message = parser.CurrentState.Switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		return await CommunicationService.SpeechAsync(parser, message, "\"");
	}

	[SharpCommand(Name = "SEMIPOSE", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> SemiPose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var message = parser.CurrentState.Switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		return await CommunicationService.SpeechAsync(parser, message, ";");
	}

	/// <summary>
	/// <c>MAT_NEAR_THINGS | MAT_CONTAINER</c> with a <c>TYPE_PLAYER</c> preference: <c>MAT_ME</c>,
	/// <c>MAT_ABSOLUTE</c>, <c>MAT_PLAYER</c>, <c>MAT_NEIGHBOR</c>, <c>MAT_POSSESSION</c> and the
	/// looker's location by name, every match required to be nearby.
	/// </summary>
	private const LocateFlags WhisperTargetFlags =
		LocateFlags.MatchMeForLooker | LocateFlags.AbsoluteMatch | LocateFlags.MatchWildCardForPlayerName |
		LocateFlags.MatchObjectsInLookerLocation | LocateFlags.MatchObjectsInLookerInventory |
		LocateFlags.MatchAgainstLookerLocationName | LocateFlags.OnlyMatchObjectsInLookerLocation |
		LocateFlags.PlayersPreference;

	/// <summary>speech.c: <c>dbref good[100]</c>.</summary>
	private const int MaxWhisperTargets = 100;

	private static readonly SearchValues<char> WhisperNameBreaks = SearchValues.Create(" \"");

	/// <summary>
	/// strutil.c <c>next_in_list</c>: spaces separate names, a leading <c>"</c> takes everything up to
	/// the next <c>"</c> as one name, and an unquoted name also stops at a <c>"</c>. Nothing else splits
	/// a name — <c>#12Lamp</c> is one token, which <c>parse_dbref</c> then refuses as a whole.
	/// </summary>
	private static IEnumerable<string> WhisperTargetNames(string list)
	{
		var head = 0;
		while (true)
		{
			while (head < list.Length && list[head] == ' ') head++;
			if (head >= list.Length) yield break;

			if (list[head] == '"')
			{
				var close = list.IndexOf('"', head + 1);
				var end = close < 0 ? list.Length : close;
				var quoted = list[(head + 1)..end];
				head = close < 0 ? list.Length : close + 1;
				if (quoted.Length > 0) yield return quoted;
				continue;
			}

			var stop = list.AsSpan(head).IndexOfAny(WhisperNameBreaks);
			var next = stop < 0 ? list.Length : head + stop;
			yield return list[head..next];
			head = next;
		}
	}

	/// <summary>
	/// PennMUSH's <c>Location()</c>, which reads the raw location field: a room's is its drop-to and an
	/// exit's its destination, where <see cref="AnySharpObject.Where"/> would answer with the room itself
	/// or the exit's source.
	/// </summary>
	private static async ValueTask<bool> LocatedIn(AnySharpObject target, DBRef location) => target switch
	{
		SharpRoom room => await room.Location.WithCancellation(CancellationToken.None) is AnySharpContainer dropTo
											&& dropTo.Object().DBRef.Equals(location),
		SharpExit exit => await exit.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
											&& destination.Object().DBRef.Equals(location),
		SharpPlayer or SharpThing => (await target.Where()).Object().DBRef.Equals(location)
	};

	[SharpCommand(Name = "WHISPER", Switches = ["LIST", "NOISY", "SILENT", "NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["player", "message"])]
	public async ValueTask<Option<CallState>> Whisper(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var switches = parser.CurrentState.Switches;

		var executorLocation = await executor.Where();

		if (switches.Contains("LIST"))
		{
			var perceive = await ObserveRealityAsync(parser, executor);
			var players = await executorLocation.Content(Mediator)
				.Where(obj => obj.IsPlayer && !obj.Object().DBRef.Equals(executor.Object().DBRef))
				.Where((item, ct) => perceive(item.Object().DBRef, ct))
				.Select(obj => obj.Object().Name)
				.ToListAsync(ExecutionBudget.CurrentToken);

			if (players.Count == 0)
			{
				await NotifyService.Notify(executor, "There is no one here to whisper to.", executor);
			}
			else
			{
				await NotifyService.Notify(executor, $"You can whisper to: {string.Join(", ", players)}", executor);
			}

			return CallState.Empty;
		}

		var isNoEval = switches.Contains("NOEVAL");
		var targetArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MarkupText.Empty);

		if (string.IsNullOrWhiteSpace(targetArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Whisper to whom?", executor);
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Whisper what?", executor);
			return CallState.Empty;
		}

		var successfulTargets = new List<AnySharpObject>();
		var unable = new List<string>();

		// speech.c do_whisper: next_in_list takes a "quoted name" whole, and each name is matched with
		// match_result(player, name, TYPE_PLAYER, MAT_NEAR_THINGS | MAT_CONTAINER). The type is a
		// preference, so any nearby object is a recipient, the whisperer included. A name that matches
		// nothing, or an object that cannot hear the whisperer, lands in one `Unable to whisper to:`
		// line — the deaf one also gets its own `can't hear you` — and the hundredth good target ends
		// the scan.
		foreach (var targetName in WhisperTargetNames(targetArg.ToPlainText()))
		{
			var found = await LocateService.Locate(parser, executor, executor, targetName, WhisperTargetFlags);
			if (found is not AnySharpObject target
					|| !await PermissionService.CanInteract(executor, target, IPermissionService.InteractType.Hear))
			{
				unable.Add(targetName.Contains(' ') ? $"\"{targetName}\"" : targetName);
				if (found is AnySharpObject deaf)
				{
					await NotifyService.Notify(executor, $"{deaf.Object().Name} can't hear you.", executor);
				}

				continue;
			}

			successfulTargets.Add(target);
			if (successfulTargets.Count >= MaxWhisperTargets)
			{
				await NotifyService.Notify(executor, "Too many people to whisper to.", executor);
				break;
			}
		}

		if (unable.Count > 0)
		{
			await NotifyService.Notify(executor, $"Unable to whisper to: {string.Join(' ', unable)}", executor);
		}

		if (successfulTargets.Count == 0)
		{
			return CallState.Empty;
		}

		// PennMUSH cmd_whisper (src/cmds.c): `noisy = SW_ISSET(NOISY) || (!SW_ISSET(SILENT) &&
		// NOISY_WHISPER)`, and `noisy` governs ONLY whether the room may overhear. The whisperer's own
		// echo is unconditional — `whisper/silent X=hi` still says "You whisper, ..." to the whisperer.
		var isNoisy = switches.Contains("NOISY")
									|| (!switches.Contains("SILENT") && Configuration.CurrentValue.Command.NoisyWhisper);

		// "Drunk wizards...": a DARK whisperer is never overheard. Otherwise each recipient rolls
		// get_random_u32(0, 100) against whisper_loudness, and one roll under it is enough — unless some
		// recipient is not standing in the whisperer's location, which keeps the whole whisper private.
		var loudness = Configuration.CurrentValue.Limit.WhisperLoudness;
		var overheard = isNoisy
										&& !await executor.IsDark()
										&& successfulTargets.Any(_ => Random.Shared.Next(0, 101) < loudness)
										&& await successfulTargets.ToAsyncEnumerable().AllAsync(async (target, _)
											=> await LocatedIn(target, executorLocation.Object().DBRef));
		var messageText = messageArg.ToPlainText();

		// PennMUSH do_whisper (src/speech.c) reads the message type off the first character exactly as
		// do_pose does: ';' is a pose with no gap, ':' a pose with one, anything else plain speech.
		// The two kinds have completely different wording — the pose kind is "senses", not "whispers".
		var gap = messageText.StartsWith(';') ? string.Empty : " ";
		var isPose = messageText.StartsWith(':') || messageText.StartsWith(';');
		var body = isPose ? messageText[1..] : messageText;

		var targetList = MessageFormatting.FormatWithOxfordComma(
			[.. successfulTargets.Select(t => t.Object().Name)]);

		if (isPose)
		{
			var sensed = $"{executor.Object().Name}{gap}{body}";
			foreach (var target in successfulTargets)
			{
				await NotifyService.Notify(target, $"You sense: {sensed}", executor, INotifyService.NotificationType.Say);
			}

			var verb = successfulTargets.Count > 1 ? "sense" : "senses";
			await NotifyService.Notify(executor, $"{targetList} {verb}: {sensed}", executor);
		}
		else
		{
			var heading = successfulTargets.Count > 1
				? $"{executor.Object().Name} whispers to {targetList}"
				: $"{executor.Object().Name} whispers";
			foreach (var target in successfulTargets)
			{
				await NotifyService.Notify(target, $"{heading}: {body}", executor, INotifyService.NotificationType.Say);
			}

			await NotifyService.Notify(executor, $"You whisper, \"{body}\" to {targetList}.", executor);
		}

		if (overheard)
		{
			var contents = executorLocation.Content(Mediator);
			await foreach (var obj in contents)
			{
				if (obj.Object().DBRef.Equals(executor.Object().DBRef) ||
						successfulTargets.Any(t => t.Object().DBRef.Equals(obj.Object().DBRef)))
				{
					continue;
				}

				await NotifyService.Notify(obj.WithRoomOption(),
					$"{executor.Object().Name} whispers to {targetList}.", executor);
			}
		}

		return new CallState(messageArg);
	}

	[SharpCommand(Name = "DOING", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Doing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var isAdmin = await executor.IsWizard() ||
									await executor.IsRoyalty() ||
									await executor.IsSee_All();

		var pattern = args.ContainsKey("0") ? args["0"].Message?.ToPlainText() : null;

		var everyone = ConnectionService.GetAll();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");

		var playerList = new List<string>();
		await foreach (var connection in everyone.Where(player => player.Ref.HasValue))
		{
			if (!isAdmin && connection.PresenceClass == PresenceClasses.Portal)
			{
				continue;
			}

			// Like WHO, a descriptor whose player is gone is left out rather than failing the listing.
			if (await Mediator.Send(new GetObjectNodeQuery(connection.Ref!.Value)) is not AnySharpObject obj)
			{
				continue;
			}

			var playerName = obj.Object().Name;

			if (!isAdmin && await obj.HasFlag("DARK"))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(pattern) && !MatchesPattern(playerName, pattern))
			{
				continue;
			}

			var doingText = await GetDoingText(executor, obj);

			playerList.Add(string.Format(
				fmt,
				playerName,
				TimeHelpers.TimeString(connection.Connected!.Value, accuracy: 3),
				TimeHelpers.TimeString(connection.Idle!.Value),
				doingText));
		}

		var footer = $"{playerList.Count} players logged in.";
		var message = $"{header}\n{string.Join('\n', playerList)}\n{footer}";

		await NotifyService.Notify(executor, message, executor);

		return new None();
	}

	private bool MatchesPattern(string playerName, string pattern)
	{
		if (pattern.Contains('*') || pattern.Contains('?'))
		{
			return MushText.IsWildcardMatch(MarkupText.Plain(playerName), pattern);
		}

		return playerName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
	}

	private async ValueTask<string> GetDoingText(AnySharpObject executor, AnySharpObject player)
	{
		var doingAttr = await AttributeService.GetAttributeAsync(
			executor,
			player,
			"DOING",
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		return doingAttr switch
		{
			SharpAttribute[] chain => chain.Last().Value.ToPlainText(),
			None or Error<string> => string.Empty
		};
	}

	[SharpCommand(Name = "SESSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Session(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var connection = await ConnectionService.Get(executor.Object().DBRef).FirstOrDefaultAsync();

		if (connection == null)
		{
			await NotifyService.Notify(executor, "No session information available.", executor);
			return CallState.Empty;
		}

		var output = new System.Text.StringBuilder();
		output.AppendLine("Session Information:");
		output.AppendLine($"  Player: {executor.Object().Name} (#{executor.Object().DBRef.Number})");

		if (connection.Connected.HasValue)
		{
			output.AppendLine($"  Connected: {TimeHelpers.TimeString(connection.Connected.Value)} ago");
		}

		if (connection.Idle.HasValue)
		{
			output.AppendLine($"  Idle: {TimeHelpers.TimeString(connection.Idle.Value)}");
		}

		if (!string.IsNullOrEmpty(connection.HostName))
		{
			output.AppendLine($"  Host: {connection.HostName}");
		}

		await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
		return CallState.Empty;
	}

	/// <summary>
	/// <c>OUTPUTPREFIX &lt;text&gt;</c> — PennMUSH src/bsd.c, <c>set_userstring(&amp;d-&gt;output_prefix, ...)</c>.
	/// A descriptor setting, not a player one: it is handled above the <c>d-&gt;connected</c> branch in
	/// <c>do_command</c>, so it answers at the connect screen too, and it applies to the socket that
	/// typed it rather than to whichever of the player's clients happens to be listed first.
	/// PennMUSH says nothing back — robot clients set this on every command and would drown in
	/// acknowledgements — so the confirmation lives only on <c>SOCKSET OUTPUTPREFIX=...</c>.
	/// </summary>
	[SharpCommand(Name = "OUTPUTPREFIX", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["prefix"])]
	public ValueTask<Option<CallState>> OutputPrefix(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetUserString(parser, "OutputPrefix");

	/// <summary>
	/// <c>OUTPUTSUFFIX &lt;text&gt;</c> — the trailing counterpart of <see cref="OutputPrefix"/>, and
	/// silent for the same reason.
	/// </summary>
	[SharpCommand(Name = "OUTPUTSUFFIX", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["suffix"])]
	public ValueTask<Option<CallState>> OutputSuffix(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetUserString(parser, "OutputSuffix");

	/// <summary>
	/// PennMUSH <c>set_userstring()</c> (src/bsd.c): leading whitespace is skipped, an otherwise empty
	/// value clears the setting, and trailing whitespace is kept — a prefix of <c>"&gt;&gt; "</c> is a
	/// legitimate thing to ask for.
	/// </summary>
	private ValueTask<Option<CallState>> SetUserString(IMUSHCodeParser parser, string key)
	{
		var connection = CurrentConnection(parser);
		if (connection is null)
		{
			return ValueTask.FromResult<Option<CallState>>(new None());
		}

		var value = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			.ToPlainText().TrimStart();

		if (string.IsNullOrEmpty(value))
		{
			connection.Metadata.TryRemove(key, out _);
		}
		else
		{
			connection.Metadata[key] = value;
		}

		return ValueTask.FromResult<Option<CallState>>(new None());
	}

	/// <summary>
	/// @locale [locale]
	/// With no argument: displays the executor's current locale.
	/// With an empty argument (@locale =): clears the locale back to the server default ("en").
	/// With a non-empty argument: validates and sets the locale for the current session and persists it
	/// as the LOCALE attribute on the player object.
	/// Locale strings are BCP-47 tags (e.g. "en", "fr", "de").
	/// </summary>
	[SharpCommand(Name = "@LOCALE", Switches = [], Behavior = CB.Default | CB.NoParse | CB.EqSplit, MinArgs = 0, MaxArgs = 1, ParameterNames = ["locale"])]
	public async ValueTask<Option<CallState>> SetLocale(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;

		// No '=' sign at all → display current locale.
		if (args.Count == 0)
		{
			var current = "en";
			var handle = parser.CurrentState.Handle;
			if (handle.HasValue)
			{
				// Use the specific connection that ran @locale to avoid multi-session ambiguity.
				var conn = ConnectionService.Get(handle.Value);
				if (conn is not null && conn.Metadata.TryGetValue("Locale", out var stored) && !string.IsNullOrEmpty(stored))
				{
					current = stored;
				}
			}
			else
			{
				// No direct handle (e.g. @force context) — fall back to persisted LOCALE attribute.
				// Through the Mediator, not the store: GetAttributeQuery is ICacheable, and reading the
				// same attribute around the cache is what leaves a write's invalidation with nothing
				// to invalidate (engine data trunk §1).
				current = await Mediator.CreateStream(new GetAttributeQuery(executor.Object().DBRef, ["LOCALE"]))
					.Select(attr => attr.Value.ToPlainText())
					.FirstOrDefaultAsync(saved => !string.IsNullOrEmpty(saved)) ?? current;
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleCurrentFormat), executor, current);
			return CallState.Empty;
		}

		var locale = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText().Trim();

		// Explicit empty argument (@locale =) → clear locale back to server default.
		if (string.IsNullOrEmpty(locale))
		{
			await AttributeService.ClearAttributeAsync(executor, executor, "LOCALE",
				IAttributeService.AttributePatternMode.Exact);

			await foreach (var conn in ConnectionService.Get(executor.Object().DBRef))
			{
				if (conn.State == IConnectionService.ConnectionState.LoggedIn)
				{
					ConnectionService.Update(conn.Handle, "Locale", string.Empty);
				}
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleCleared), executor);
			return CallState.Empty;
		}

		System.Globalization.CultureInfo? culture;
		try
		{
			culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
		}
		catch (System.Globalization.CultureNotFoundException)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleInvalidFormat), executor, locale);
			return CallState.Empty;
		}

		var canonicalLocale = culture.Name; // e.g. "en-US" → "en-US", "fr" → "fr"

		// Persist to the player's LOCALE attribute so it survives reconnects.
		await AttributeService.SetAttributeAsync(executor, executor, "LOCALE", MarkupText.Plain(canonicalLocale));

		await foreach (var conn in ConnectionService.Get(executor.Object().DBRef))
		{
			if (conn.State == IConnectionService.ConnectionState.LoggedIn)
			{
				ConnectionService.Update(conn.Handle, "Locale", canonicalLocale);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleSetFormat), executor, canonicalLocale);
		return CallState.Empty;
	}

	[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> WarnOnMissing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Internal no-op command for warning system
		await ValueTask.CompletedTask;
		return new None();
	}

	[SharpCommand(Name = "UNIMPLEMENTED_COMMAND", Switches = [],
		Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> UnimplementedCommand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator);
		await NotifyService.Notify(executor, "Huh?  (Type \"help\" for help.)", executor);
		return new None();
	}
}
