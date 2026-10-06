using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using DotNext.Collections.Generic;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@QUEUE", Switches = ["LIST", "PAUSE", "RESUME", "OWNER", "OBJECT"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2, ParameterNames = ["pid or target", "reason"])]
	public async ValueTask<Option<CallState>> QueueControl(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		try { return await QueueControlCore(parser, executor); }
		catch (NotSupportedException) { return await QueueInspectionUnsupported(executor); }
	}

	private async ValueTask<Option<CallState>> QueueControlCore(IMUSHCodeParser parser, AnySharpObject executor)
	{
		var switches = parser.CurrentState.Switches;
		var pause = switches.Contains("PAUSE");
		var resume = switches.Contains("RESUME");
		var owner = switches.Contains("OWNER");
		var source = switches.Contains("OBJECT");
		var args = parser.CurrentState.Arguments;
		var selection = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";
		var reason = args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "";
		if ((pause && resume) || ((pause || resume) && switches.Contains("LIST")) || (owner && source)
			|| (!pause && args.Count > 1) || ((pause || resume || owner || source) && string.IsNullOrWhiteSpace(selection)))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlSyntax), executor);
			return new CallState(ErrorMessages.Returns.InvalidTarget);
		}
		if (reason.Length > 160 || reason.Any(char.IsControl))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlBadReason), executor);
			return new CallState(ErrorMessages.Returns.InvalidTarget);
		}
		var ct = ExecutionBudget.CurrentToken;
		var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGameActorAsync(executor.Object().DBRef, ct);
		if (actor is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
		var service = parser.ServiceProvider.GetRequiredService<IQueueControlService>();
		var queueReader = parser.ServiceProvider.GetRequiredService<ITaskQueueReader>();
		IEnumerable<QueueEntrySnapshot> entries = pause || resume ? queueReader.GetQueueEntries() : await service.ListAsync(actor, ct);
		if (owner || source)
		{
			// Bulk selection uses only inspectable records; each mutation independently rechecks control.
			if (pause || resume) entries = await service.ListAsync(actor, ct);
			var found = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, selection, LocateFlags.All);
			if (found is not AnySharpObject foundObject) return new CallState(ErrorMessages.Returns.InvalidTarget);
			var target = foundObject.Object().DBRef;
			entries = entries.Where(e => (owner ? e.Owner : e.Source) == target);
			if (pause || resume) entries = entries.Where(e => e.State == (pause ? QueueEntryState.Pending : QueueEntryState.Paused));
		}
		else if (!string.IsNullOrWhiteSpace(selection))
		{
			if (!long.TryParse(selection, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
				return new CallState(ErrorMessages.Returns.InvalidPid);
			if (pause || resume)
			{
				var result = await service.ChangeAsync(actor, pid, resume, reason, ct);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlOutcome), executor, pid, result);
				return CallState.Empty;
			}
			entries = entries.Where(e => e.Pid == pid);
		}
		var batch = entries.OrderBy(e => e.Pid).Take(201).ToArray();
		foreach (var entry in batch.Take(200))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			if (pause || resume)
			{
				var result = await service.ChangeAsync(actor, entry.Pid, resume, reason, ct);
				// An inaccessible record must not disclose its PID through a bulk operation.
				if (result != QueueControlResult.NotFound)
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlOutcome), executor, entry.Pid, result);
			}
			else
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlEntry), executor,
					entry.Pid, entry.Source?.ToString() ?? "?", entry.Owner?.ToString() ?? "?", entry.Kind, entry.State,
					entry.RemainingDelay?.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) ?? "-", entry.ReleasePending, entry.PauseReason);
		}
		if (batch.Length > 200)
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlTruncated), executor);
		else if (batch.Length == 0)
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlEmpty), executor);
		return CallState.Empty;
	}

	private const string DefaultSemaphoreAttribute = "SEMAPHORE";

	private static readonly string[] DefaultSemaphoreAttributeArray = [DefaultSemaphoreAttribute];

	/// <summary>
	/// Validates that a custom semaphore attribute follows the required rules:
	/// 1. If already set, must have same owner (God) and flags as SEMAPHORE (no_inherit, no_clone, locked)
	/// 2. If already set, must have numeric or empty value
	/// 3. If not set, cannot be a built-in attribute (unless it is SEMAPHORE)
	/// </summary>
	private async ValueTask<Result<Success>> ValidateSemaphoreAttribute(
		AnySharpObject targetObject,
		string[] attributePath)
	{
		if (attributePath.Length == 1 && attributePath[0].Equals(DefaultSemaphoreAttribute, StringComparison.OrdinalIgnoreCase))
		{
			return new Success();
		}

		var allStandardAttributes = Mediator.CreateStream(new GetAllAttributeEntriesQuery(), ExecutionBudget.CurrentToken);
		var isStandardAttribute = await allStandardAttributes
			.AnyAsync(stdAttr => stdAttr.Name.Equals(attributePath[0], StringComparison.OrdinalIgnoreCase), ExecutionBudget.CurrentToken);

		if (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken) is not AnySharpObject god)
		{
			throw new InvalidOperationException("God (#1) must exist.");
		}

		return await AttributeService.GetAttributeAsync(
			god, targetObject, string.Join("`", attributePath), IAttributeService.AttributeMode.Read, false) switch
		{
			SharpAttribute[] chain => await ValidateExistingSemaphoreAttribute(chain.Last()),
			None when isStandardAttribute => new Error<string>($"Cannot use built-in attribute '{attributePath[0]}' as semaphore."),
			None => new Success(),
			Error<string> error => error
		};
	}

	/// <summary>The rules an attribute already on the object must meet to be used as a semaphore.</summary>
	private static async ValueTask<Result<Success>> ValidateExistingSemaphoreAttribute(SharpAttribute attribute)
	{

		// Note: Owner is guaranteed to exist for attributes
		var owner = await attribute.Owner.WithCancellation(ExecutionBudget.CurrentToken);
		if (owner!.Object.Key != 1)
		{
			return new Error<string>($"Semaphore attribute must be owned by God (#1). Current owner: #{owner.Object.Key}");
		}

		var value = attribute.Value.ToPlainText();
		if (!string.IsNullOrEmpty(value) && !int.TryParse(value, out _))
		{
			return new Error<string>($"Semaphore attribute must have a numeric or empty value. Current value: {value}");
		}

		var flagNames = attribute.Flags.Select(f => f.Name.ToLowerInvariant()).ToHashSet();
		var requiredFlags = new[] { "no_inherit", "no_clone", "locked" };
		var missingFlags = requiredFlags.Except(flagNames).ToList();

		if (missingFlags.Any())
		{
			return new Error<string>($"Semaphore attribute must have flags: {string.Join(", ", requiredFlags)}. Missing: {string.Join(", ", missingFlags)}");
		}

		return new Success();
	}

	/// <remarks>
	/// PennMUSH <c>cmd_halt</c> (<c>src/cmds.c:661-669</c>): <c>/all</c> is <c>do_allhalt</c>, <c>/pid</c>
	/// is <c>do_haltpid</c>, anything else <c>do_halt1</c> (<c>src/cque.c:2236-2280</c>). Every queue wipe
	/// goes through <see cref="HaltQueuesAsync"/>, which is <c>do_halt</c> and carries its report to the
	/// victim's owner.
	/// </remarks>
	[SharpCommand(Name = "@HALT", Switches = ["ALL", "NOEVAL", "PID"], Behavior = CB.Default | CB.EqSplit | CB.RSBrace,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Halt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (switches.Contains("ALL"))
		{
			return await HaltWorldAsync(executor);
		}

		if (switches.Contains("PID"))
		{
			return await HaltPidAsync(parser, executor, args.GetValueOrDefault("0")?.Message?.ToPlainText());
		}

		// do_halt1 (src/cque.c:2239-2240): no object halts the enactor, and leaves its HALT flag alone.
		var targetName = args.GetValueOrDefault("0")?.Message?.ToPlainText();
		if (string.IsNullOrEmpty(targetName))
		{
			await HaltQueuesAsync(executor);
			return CallState.Empty;
		}

		var maybeTarget = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (maybeTarget is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var controls = await PermissionService.Controls(executor, target);
		if (!controls && !await HaltsAnything(executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// RSBrace preserves outer braces during argument parsing (PennMUSH CS_BRACES).
		// Strip them here before execution (PennMUSH PE_COMMAND_BRACES equivalent).
		var replacementActions = args.GetValueOrDefault("1")?.Message is { } actions
			? HelperFunctions.StripOuterBraces(actions)
			: null;
		var hasReplacementActions = replacementActions is not null && replacementActions.Length > 0;

		if (hasReplacementActions && !controls)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltCommandNotAllowed), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var targetObject = target.Object();
		var executorObject = executor.Object();
		await HaltQueuesAsync(target);

		if (hasReplacementActions)
		{
			await Mediator.Send(new AdmitCommandListRequest(
				replacementActions!,
				parser.CurrentState,
				new DbRefAttribute(targetObject.DBRef, DefaultSemaphoreAttributeArray),
				-1), ExecutionBudget.CurrentToken);
		}

		if (target.IsPlayer)
		{
			if (targetObject.DBRef.Number == executorObject.DBRef.Number)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllYourObjectsHalted), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllObjectsForPlayerHaltedFormat),
					executor, targetObject.Name);
				await NotifyService.NotifyLocalized(target, nameof(ErrorMessages.Notifications.AllYourObjectsHaltedByFormat),
					executor, executorObject.Name);
			}

			return CallState.Empty;
		}

		// src/cque.c:2267-2275 compares the owner with the enactor itself, not with the enactor's owner.
		var owner = (await targetObject.Owner.WithCancellation(CancellationToken.None)).Object;
		if (owner.DBRef.Number != executorObject.DBRef.Number)
		{
			var dbref = $"#{targetObject.DBRef.Number}";
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltedOthersObjectFormat),
				executor, owner.Name, targetObject.Name, dbref);
			await NotifyService.NotifyLocalized(owner.DBRef, nameof(ErrorMessages.Notifications.HaltedObjectByFormat),
				sender: executor, targetObject.Name, dbref, executorObject.Name);
		}

		// src/cque.c:2277-2278: only a halt without replacement actions leaves the object HALTed.
		if (!hasReplacementActions)
		{
			var haltFlag = await Mediator.Send(new GetObjectFlagQuery("HALT"));
			if (haltFlag != null)
			{
				await Mediator.Send(new SetObjectFlagCommand(target, haltFlag));
			}
		}

		return CallState.Empty;
	}

	/// <summary>PennMUSH <c>HaltAny</c> (<c>hdrs/mushdb.h:38</c>): a wizard, or the HALT power.</summary>
	private static async ValueTask<bool> HaltsAnything(AnySharpObject executor)
		=> await executor.IsWizard() || await executor.HasPower("HALT");

	/// <summary>
	/// PennMUSH <c>do_halt</c> (<c>src/cque.c:2160-2220</c>): tells the victim's owner
	/// <c>Halted: &lt;name&gt;(#&lt;dbref&gt;)</c> unless that owner is QUIET, then wipes every queue
	/// entry the victim runs — and, for a player, every entry an object they own runs.
	/// </summary>
	private async ValueTask HaltQueuesAsync(AnySharpObject victim)
		=> await HaltQueuesAsync(victim, (await victim.Object().Owner.WithCancellation(CancellationToken.None)).Object);

	/// <inheritdoc cref="HaltQueuesAsync(AnySharpObject)"/>
	/// <param name="victim">The object whose queue is wiped.</param>
	/// <param name="owner">Its owner, given by a caller that has just changed it (<c>chown_object</c>).</param>
	private async ValueTask HaltQueuesAsync(AnySharpObject victim, SharpObject owner)
	{
		var victimObject = victim.Object();
		if (!await owner.HasQuietFlagAsync())
		{
			await NotifyService.NotifyLocalized(owner.DBRef, nameof(ErrorMessages.Notifications.HaltedNoticeFormat),
				sender: null, victimObject.Name, $"#{victimObject.DBRef.Number}");
		}

		await Mediator.Send(new HaltObjectQueueRequest(victimObject.DBRef));
		if (!victim.IsPlayer) return;

		// The owner index names the player's objects; the world is not scanned for them. Halting changes
		// no ownership, so the stream is read as it goes.
		await foreach (var obj in Mediator.CreateStream(new GetFilteredObjectsQuery(
			new ObjectSearchFilter { Owner = victimObject.DBRef })))
		{
			if (obj.DBRef.Number == victimObject.DBRef.Number) continue;
			await Mediator.Send(new HaltObjectQueueRequest(obj.DBRef));
		}
	}

	/// <summary>
	/// PennMUSH <c>do_allhalt</c> (<c>src/cque.c:2343-2358</c>), for <c>@halt/all</c> and <c>@allhalt</c>:
	/// every player is told who halted the world, then halted as <c>do_halt</c> halts them. The enactor
	/// hears nothing else.
	/// </summary>
	private async ValueTask<CallState> HaltWorldAsync(AnySharpObject executor)
	{
		if (!await HaltsAnything(executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltWorldPowerDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var executorName = executor.Object().Name;
		await foreach (var obj in Mediator.CreateStream(new GetAllTypedObjectsQuery()))
		{
			var objObject = obj.Object();
			if (obj.IsPlayer)
			{
				await NotifyService.NotifyLocalized(obj, nameof(ErrorMessages.Notifications.GloballyHaltedByFormat),
					executor, executorName);
				if (!await objObject.HasQuietFlagAsync())
				{
					await NotifyService.NotifyLocalized(obj, nameof(ErrorMessages.Notifications.HaltedNoticeFormat),
						sender: null, objObject.Name, $"#{objObject.DBRef.Number}");
				}
			}

			// A player's do_halt also wipes what everything they own has queued; one pass over every
			// object reaches the same entries.
			await Mediator.Send(new HaltObjectQueueRequest(objObject.DBRef));
		}

		return CallState.Empty;
	}

	/// <summary>PennMUSH <c>do_haltpid</c> (<c>src/cque.c:2287-2337</c>).</summary>
	private async ValueTask<Option<CallState>> HaltPidAsync(IMUSHCodeParser parser, AnySharpObject executor, string? pidStr)
	{
		// is_strict_uinteger (src/cque.c:2293): an empty argument is not a pid either. Penn has no return
		// value to give; the error returns stay SharpMUSH's own.
		if (string.IsNullOrEmpty(pidStr))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPid), executor);
			return new CallState(ErrorMessages.Returns.NoPidSpecified);
		}

		if (!long.TryParse(pidStr, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPid), executor);
			return new CallState(ErrorMessages.Returns.InvalidPid);
		}

		var queueReader = parser.ServiceProvider.GetRequiredService<ITaskQueueReader>();
		if (!TryGetQueueEntry(queueReader, pid, out var entry)) return await QueueInspectionUnsupported(executor);
		if (entry is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPid), executor);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		if (!await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
			.CanAccessLegacyAsync(executor, pid, mutate: true, ExecutionBudget.CurrentToken))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!await Mediator.Send(new HaltByPidRequest(pid), ExecutionBudget.CurrentToken))
		{
			// It ran or left the queue between the lookup and the halt: it is no longer a pid.
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPid), executor);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltPidHaltedFormat), executor, pid);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@NOTIFY", Switches = ["ALL", "ANY", "SETQ", "QUIET"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
		MinArgs = 1, MaxArgs = 0, ParameterNames = ["target", "message"])]
	public async ValueTask<Option<CallState>> Notify(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.Except(["QUIET"]).ToArray();
		var notifyType = "ANY";
		var args = parser.CurrentState.Arguments;

		if ((parser.CurrentState.Arguments.Count == 0) || string.IsNullOrEmpty(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifySemaphoreObject), executor);
			return new None();
		}

		switch (switches)
		{
			case ["ALL"]:
				notifyType = "ALL";
				break;
			case ["ANY"]:
				notifyType = "ANY";
				break;
			case ["SETQ"]:
				notifyType = "SETQ";
				break;
			case []:
				break;
			default:
				return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		if (HelperFunctions.SplitDbRefAndOptionalAttr(args["0"].Message!.ToPlainText()) is not { Object: var db, Attribute: var maybeAttributeString })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifyValidObjectAttribute), executor);
			return new None();
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
			db, LocateFlags.All) switch
		{
			AnySharpObject objectToNotify => await NotifySemaphoreHolderAsync(parser, executor, objectToNotify, notifyType,
				args, maybeAttributeString),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> NotifySemaphoreHolderAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject objectToNotify, string notifyType, Dictionary<string, CallState> args, string? maybeAttributeString)
	{
		if (!await PermissionService.Controls(executor, objectToNotify) &&
			!await objectToNotify.HasFlag("LINK_OK"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var attribute = string.IsNullOrEmpty(maybeAttributeString) ? DefaultSemaphoreAttribute : maybeAttributeString;

		using var semaphoreMutation = await parser.ServiceProvider.GetRequiredService<ISemaphoreQueue>().EnterSemaphoreMutationAsync();
		var attributeContents = await AttributeService.GetAttributeAsync(executor, objectToNotify, attribute,
			IAttributeService.AttributeMode.Execute, false);

		if (attributeContents is Error<string> attributeError)
		{
			return new CallState(attributeError.Value);
		}

		int notifyCount = 1;
		Dictionary<string, MString>? qRegisters = null;

		if (notifyType == "SETQ")
		{
			// With CB.RSArgs, each comma-separated value becomes a separate argument
			// So @notify/setq obj=0,val1,1,val2 becomes: args[0]=obj, args[1]=0, args[2]=val1, args[3]=1, args[4]=val2
			if (await RejectIfTooFewArguments(parser, 3, executor,
					nameof(ErrorMessages.Notifications.NotifyMustSpecifyQregAssignments), ErrorMessages.Returns.MissingQregAssignments) is { } usage)
			{
				return usage;
			}

			var qregArgCount = args.Count - 1;
			if (qregArgCount % 2 != 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyQregAssignmentsMustBePairs), executor);
				return new CallState(ErrorMessages.Returns.InvalidQregPairs);
			}

			qRegisters = new Dictionary<string, MString>();
			for (var i = 1; i < args.Count; i += 2)
			{
				var qregName = args[i.ToString()].Message!.ToPlainText().Trim();
				var qregValue = args[(i + 1).ToString()].Message!.ToPlainText();
				qRegisters[qregName] = MarkupText.Plain(qregValue);
			}
		}
		else if (args.Count > 1 && args.TryGetValue("1", out var arg1))
		{
			var countArg = arg1.Message?.ToPlainText();
			if (!string.IsNullOrEmpty(countArg) &&
					(!int.TryParse(countArg, out notifyCount) || notifyCount < 1))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyInvalidNumber), executor);
				return new CallState(ErrorMessages.Returns.InvalidNumber);
			}
		}

		var dbRefAttribute = new DbRefAttribute(objectToNotify.Object().DBRef, attribute.Split("`"));
		var validation = await ValidateSemaphoreAttribute(objectToNotify, dbRefAttribute.Attribute);
		if (validation is Error<string> validationError) return await ReportSemaphoreCommandError(executor, validationError.Value);
		var semaphores = parser.ServiceProvider.GetRequiredService<ISemaphoreQueue>();
		return await SemaphoreCommandAccounting(objectToNotify, dbRefAttribute.Attribute,
			(old, selected) => notifyType == "ALL" ? Math.Max(0, (long)old - selected) : (long)old - (notifyType == "SETQ" ? 1 : notifyCount), false) switch
		{
			SemaphoreAccounting counted => await NotifySemaphoreAsync(parser, executor, semaphores, dbRefAttribute, notifyType,
				notifyCount, qRegisters, counted),
			Error<string> accountingError => await ReportSemaphoreCommandError(executor, accountingError.Value),
		};
	}

	/// <summary>
	/// The half of <c>@notify</c> that releases the waiting tasks, once the semaphore's count has been accounted for.
	/// </summary>
	private async ValueTask<Option<CallState>> NotifySemaphoreAsync(IMUSHCodeParser parser, AnySharpObject executor,
		ISemaphoreQueue semaphores, DbRefAttribute dbRefAttribute, string notifyType, int notifyCount,
		Dictionary<string, MString>? qRegisters, SemaphoreAccounting counted)
	{
		var changed = await semaphores.ApplySemaphoreCommandAsync(dbRefAttribute,
			notifyType == "ALL" ? null : notifyType == "SETQ" ? 1 : notifyCount, false,
			counted.Persist, counted.Reconcile, qRegisters);
		if (notifyType == "SETQ" && changed == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyNoTaskWaitingOnSemaphore), executor);
			return new CallState(ErrorMessages.Returns.NoWaitingTask);
		}

		// cmd_notify_drain says "Notified." through quiet_notify, which an executor that is QUIET, or
		// whose owner is, does not hear (src/cque.c:1509, :1541; hdrs/notify.h:153-155) — /setq too.
		if (!parser.CurrentState.Switches.Contains("QUIET") && !await executor.Object().IsQuietAsync())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Notified), executor);
		}

		return new None();
	}

	[SharpCommand(Name = "@WAIT", Switches = ["PID", "UNTIL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 1, MaxArgs = 2, ParameterNames = ["seconds", "command"])]
	public async ValueTask<Option<CallState>> Wait(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? string.Empty;
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// RSBrace preserves outer braces during argument parsing (PennMUSH CS_BRACES).
		// Strip them here before execution (PennMUSH PE_COMMAND_BRACES equivalent).
		if (arg1 is not null)
			arg1 = HelperFunctions.StripOuterBraces(arg1);

		// Restore caller's pattern-match args (%0-%9) for the queued callback state.
		// Without this, %0 inside @wait callbacks would resolve to @wait's own arg (the delay time)
		// instead of the enclosing $command pattern match. Equivalent to PennMUSH wenv preservation.
		var callbackState = parser.CurrentState.CallerArguments is not null
			? parser.CurrentState with { Arguments = new Dictionary<string, CallState>(parser.CurrentState.CallerArguments) }
			: parser.CurrentState;

		if (switches.Contains("PID"))
		{
			return await AtWaitForPid(parser, arg0, executor, arg1?.ToPlainText(), switches);
		}

		if (arg1 is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitCommandListMissing), executor);
			return new CallState(ErrorMessages.Returns.MissingCommandListArgument);
		}

		if (double.TryParse(arg0, out var time))
		{
			TimeSpan convertedTime;
			if (switches.Contains("UNTIL"))
			{
				convertedTime = DateTimeOffset.FromUnixTimeSeconds((long)time) - DateTimeOffset.UtcNow;
			}
			else
			{
				convertedTime = TimeSpan.FromSeconds(time);
			}

			await Mediator.Send(new AdmitDelayedCommandListRequest(arg1, callbackState, convertedTime), ExecutionBudget.CurrentToken);
			return CallState.Empty;
		}

		var splitBySlashes = arg0.Split('/');

		if (splitBySlashes.Length == 1)
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
				LocateFlags.All, async located =>
				{
					if (!await PermissionService.Controls(executor, located))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
						return new CallState(ErrorMessages.Returns.PermissionDenied);
					}

					await QueueSemaphore(parser, located, DefaultSemaphoreAttributeArray, arg1, callbackState);
					return CallState.Empty;
				});
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, splitBySlashes[0],
				LocateFlags.All) switch
		{
			AnySharpObject foundObject => await WaitOnObjectAsync(parser, executor, foundObject, arg1, switches,
				callbackState, splitBySlashes),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> WaitOnObjectAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject foundObject, MString arg1, string[] switches, ParserState callbackState, string[] splitBySlashes)
	{
		var untilTime = 0.0d;

		switch (splitBySlashes.Length)
		{
			case 2 when switches.Contains("UNTIL"):
				{
					if (!double.TryParse(splitBySlashes[1], out untilTime))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeArgumentFormat), executor);
						return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "TIME ARGUMENT"));
					}

					var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;

					await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, newUntilTime, arg1, callbackState);
					return CallState.Empty;
				}

			case 2 when double.TryParse(splitBySlashes[1], out untilTime):
				await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, TimeSpan.FromSeconds(untilTime), arg1, callbackState);
				return CallState.Empty;

			case 2:
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation is Error<string> error)
					{
						await NotifyService.Notify(executor, error.Value, executor);
						return new CallState(ErrorMessages.Returns.InvalidSemaphoreAttribute);
					}

					await QueueSemaphore(parser, foundObject, customSemaphoreAttr, arg1, callbackState);
					return CallState.Empty;
				}

			case 3 when !double.TryParse(splitBySlashes[2], out untilTime):
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeArgumentFormat), executor);
				return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "TIME ARGUMENT"));

			// Note: Attribute value validation for semaphore usage is handled in QueueSemaphore/QueueSemaphoreWithDelay
			// methods. If the attribute value is not a valid integer, an error is returned.
			case 3 when switches.Contains("UNTIL"):
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation is Error<string> error)
					{
						await NotifyService.Notify(executor, error.Value, executor);
						return new CallState(ErrorMessages.Returns.InvalidSemaphoreAttribute);
					}

					var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;
					await QueueSemaphoreWithDelay(parser, foundObject, customSemaphoreAttr, newUntilTime, arg1, callbackState);
					return CallState.Empty;
				}

			case 3:
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation is Error<string> error)
					{
						await NotifyService.Notify(executor, error.Value, executor);
						return new CallState(ErrorMessages.Returns.InvalidSemaphoreAttribute);
					}

					await QueueSemaphoreWithDelay(parser, foundObject, customSemaphoreAttr,
						TimeSpan.FromSeconds(untilTime), arg1, callbackState);
					return CallState.Empty;
				}

			default:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidFirstArgumentFormat), executor);
				return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "FIRST ARGUMENT"));
		}
	}

	private ValueTask QueueSemaphore(IMUSHCodeParser parser, AnySharpObject located, string[] attribute,
		MString arg1, ParserState? callbackState = null)
		=> QueueSemaphoreWithDelay(parser, located, attribute, TimeSpan.FromDays(36500), arg1, callbackState);

	private async ValueTask QueueSemaphoreWithDelay(IMUSHCodeParser parser, AnySharpObject located,
		string[] attribute, TimeSpan delay, MString arg1, ParserState? callbackState = null)
	{
		var token = ExecutionBudget.CurrentToken;
		var attrValue = await Mediator.CreateStream(new GetAttributeQuery(located.Object().DBRef, attribute), token).LastOrDefaultAsync(token);
		if (attrValue is not null && attrValue.Value.Length > 0 && !int.TryParse(attrValue.Value.ToPlainText(), out _))
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			await NotifyService.Notify(executor, ErrorMessages.Returns.Integer, executor);
			return;
		}
		// Admission owns the counter transaction, including negative credits and schedule rollback.
		await Mediator.Send(new AdmitCommandListWithTimeoutRequest(arg1, callbackState ?? parser.CurrentState,
			new DbRefAttribute(located.Object().DBRef, attribute), 0, delay, ManageSemaphoreCount: true), token);
	}

	private async ValueTask<Option<CallState>> AtWaitForPid(IMUSHCodeParser parser, string? arg0,
		AnySharpObject executor, string? arg1,
		string[] switches)
	{
		if (!long.TryParse(arg0, out var pid))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidPidSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidPid);
		}

		if (string.IsNullOrEmpty(arg1))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitWhatToDoWithProcess), executor);
			return new CallState(string.Format(ErrorMessages.Returns.TooFewArguments, "@WAIT", 2, 1));
		}

		if (!TryGetQueueEntry(parser.ServiceProvider.GetRequiredService<ITaskQueueReader>(), pid, out var maybeFoundPid))
			return await QueueInspectionUnsupported(executor);

		if (maybeFoundPid is null || maybeFoundPid.RemainingDelay is null || maybeFoundPid.ReleasePending)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidPidSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidPid);
		}

		if (!await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
			.CanAccessLegacyAsync(executor, pid, mutate: true, ExecutionBudget.CurrentToken))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!long.TryParse(arg1, System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture, out var seconds))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidTime);
		}

		TimeSpan delay;
		try
		{
			var now = DateTimeOffset.UtcNow;
			delay = switches.Contains("UNTIL")
				? DateTimeOffset.FromUnixTimeSeconds(seconds) - now
				: arg1.StartsWith('+') || arg1.StartsWith('-')
					? maybeFoundPid.RemainingDelay.Value + TimeSpan.FromSeconds(seconds)
					: TimeSpan.FromSeconds(seconds);
			if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
			if (delay > DateTimeOffset.MaxValue - now) throw new ArgumentOutOfRangeException(nameof(seconds));
		}
		catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidTime);
		}

		await Mediator.Send(new RescheduleSemaphoreRequest(pid, delay));
		return new CallState(pid.ToString());
	}

	[SharpCommand(Name = "@DRAIN", Switches = ["ALL", "ANY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["object", "attribute"])]
	public async ValueTask<Option<CallState>> Drain(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message?.ToPlainText();
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (switches.Length > 1)
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.TooManySwitches, executor);
			return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		if (HelperFunctions.SplitDbRefAndOptionalAttr(arg0) is not { Object: var target, Attribute: var maybeAttribute })
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.CantSeeThat, executor);
			return new CallState(ErrorMessages.Returns.CantSeeThat);
		}

		return await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target,
			LocateFlags.All) switch
		{
			AnySharpObject objectToDrain => await DrainObjectAsync(parser, executor, objectToDrain, switches, arg1,
				maybeAttribute),
			None => new CallState(ErrorMessages.Returns.CantSeeThat),
			Error<string> error => new CallState(error.Value)
		};
	}

	private async ValueTask<Option<CallState>> DrainObjectAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject objectToDrain, string[] switches, string? arg1, string? maybeAttribute)
	{
		if (!await PermissionService.Controls(executor, objectToDrain) &&
			!await objectToDrain.HasFlag("LINK_OK"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
		var attribute = maybeAttribute?.Split("`") ?? DefaultSemaphoreAttributeArray;
		var hasAll = switches.Contains("ALL");
		var hasAny = switches.Contains("ANY");

		int? drainCount = null;
		if (!string.IsNullOrEmpty(arg1))
		{
			if (!int.TryParse(arg1, out var count) || count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyInvalidNumber), executor);
				return new CallState(ErrorMessages.Returns.InvalidNumber);
			}
			drainCount = count;
		}

		if (hasAny && maybeAttribute is not null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DrainCannotSpecifyBothAnyAndAttribute), executor);
			return new CallState(ErrorMessages.Returns.InvalidCombination);
		}

		if (hasAll && drainCount.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DrainCannotSpecifyBothAllAndNumber), executor);
			return new CallState(ErrorMessages.Returns.InvalidCombination);
		}

		using var semaphoreMutation = await parser.ServiceProvider.GetRequiredService<ISemaphoreQueue>().EnterSemaphoreMutationAsync();
		async ValueTask<CallState?> DrainAttribute(DbRefAttribute target)
		{
			var validation = await ValidateSemaphoreAttribute(objectToDrain, target.Attribute);
			if (validation is Error<string> validationError) return await ReportSemaphoreCommandError(executor, validationError.Value);
			return await SemaphoreCommandAccounting(objectToDrain, target.Attribute,
				(old, selected) => drainCount.HasValue && old < 0 ? old : Math.Max(0, (long)old - selected), true) switch
			{
				SemaphoreAccounting counted => await DrainCounted(target, counted),
				Error<string> accountingError => await ReportSemaphoreCommandError(executor, accountingError.Value),
			};
		}

		async ValueTask<CallState?> DrainCounted(DbRefAttribute target, SemaphoreAccounting counted)
		{
			await parser.ServiceProvider.GetRequiredService<ISemaphoreQueue>().ApplySemaphoreCommandAsync(target,
				drainCount, true, counted.Persist, counted.Reconcile);
			return null;
		}

		if (hasAny)
		{
			var pids = Mediator.CreateStream(new ScheduleSemaphoreQuery(objectToDrain.Object().DBRef), ExecutionBudget.CurrentToken);
			var filteredPids = pids
				.GroupBy(data => string.Join('`', data.SemaphoreSource.Attribute), x => x.SemaphoreSource)
				.Select(x => x.First());
			await foreach (var uniqueAttribute in filteredPids.WithCancellation(ExecutionBudget.CurrentToken))
			{
				if (await DrainAttribute(uniqueAttribute) is { } error) return error;
			}
		}
		else
		{
			if (await DrainAttribute(new DbRefAttribute(objectToDrain.Object().DBRef, attribute)) is { } error) return error;
		}

		// cmd_notify_drain: `quiet_notify(executor, T("Drained."))` (src/cque.c:1539).
		if (!await executor.Object().IsQuietAsync())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Drained), executor);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@FORCE", Output = CommandOutput.Runs, Switches = ["NOEVAL", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "command"])]
	public async ValueTask<Option<CallState>> Force(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var objArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty);
		var cmdListArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// RSBrace preserves outer braces during argument parsing (PennMUSH CS_BRACES).
		// Strip them here before execution (PennMUSH PE_COMMAND_BRACES equivalent).
		cmdListArg = HelperFunctions.StripOuterBraces(cmdListArg);

		// do_force (PennMUSH src/wiz.c:636-638): whatever match_controlled refused, it has already said
		// why, and do_force adds "Sorry." after it.
		switch (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objArg.ToPlainText(),
					LocateFlags.All))
		{
			case AnySharpObject found:
				return await ForceAsync(parser, executor, found, cmdListArg);
			case Error<CallState> error:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ForceSorry), executor);
				return error.Value;
			default:
				return new None();
		}
	}

	private async ValueTask<Option<CallState>> ForceAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject found, MString cmdListArg)
	{
		// God cannot be forced by anyone (PennMUSH src/wiz.c).
		if (found.IsGod() && !executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantForceGod), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// match_controlled (src/match.c:104, MAT_CONTROL) answers "Permission denied." for an object the
		// forcer can see but not control; do_force then says "Sorry." (src/wiz.c:636-638).
		if (!await PermissionService.Controls(executor, found))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ForceSorry), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (cmdListArg.Length < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ForceThemToDoWhat), executor);
			return new CallState(ErrorMessages.Returns.NothingToDo);
		}

		var switches = parser.CurrentState.Switches;
		var hasLocalize = switches.Contains("LOCALIZE");
		var hasClearRegs = switches.Contains("CLEARREGS");

		// /LOCALIZE: forced code cannot permanently change the caller's Q-registers.
		// /CLEARREGS: it starts with empty Q-registers.
		using var registers = RegisterScope.Enter(parser.CurrentState.Registers, hasLocalize, hasClearRegs);

		// Note: Queue infrastructure available via AdmitCommandListRequest if needed
		// Currently executes inline for immediate response (default PennMUSH behavior)
		var nestedResult = await parser.With(
			state => state with
			{
				Executor = found.Object().DBRef,
				Caller = state.Executor
			},
			async newParser => await newParser.CommandListParseVisitor(cmdListArg)());

		return CallState.Empty with { HadErrors = nestedResult?.HadErrors == true };
	}

	private static bool TryGetQueueEntry(ITaskQueueReader queue, long pid,
		out SharpMUSH.Library.Models.SchedulerModels.QueueEntrySnapshot? entry)
	{
		try { entry = queue.GetQueueEntry(pid); return true; }
		catch (NotSupportedException) { entry = null; return false; }
	}

	private async ValueTask<Option<CallState>> QueueInspectionUnsupported(AnySharpObject executor)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotSupportedForSharpMUSH), executor);
		return new CallState(ErrorMessages.Returns.ErrorNotSupported);
	}

	[SharpCommand(Name = "@PS", Switches = ["ALL", "SUMMARY", "COUNT", "QUICK", "DEBUG", "HISTORY"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["player, pid, or history-limit"])]
	public async ValueTask<Option<CallState>> ProcessStatus(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (parser.CurrentState.Switches.Contains("HISTORY")) return await QueueHistory(parser);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var queueReader = parser.ServiceProvider.GetRequiredService<ITaskQueueReader>();

		if (switches.Contains("DEBUG"))
		{
			var pidStr = args.GetValueOrDefault("0")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(pidStr))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltMustSpecifyPid), executor);
				return new CallState(ErrorMessages.Returns.NoPidSpecified);
			}

			if (!long.TryParse(pidStr, out var pid))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPidFormat), executor);
				return new CallState(ErrorMessages.Returns.InvalidPid);
			}

			if (!TryGetQueueEntry(queueReader, pid, out var queued)) return await QueueInspectionUnsupported(executor);
			if (queued is null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsNoTaskWithPidFormat), executor, pid);
				return new CallState(ErrorMessages.Returns.NotFound);
			}
			if (!await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
				.CanAccessLegacyAsync(executor, pid, mutate: false, ExecutionBudget.CurrentToken))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var task = await Mediator.CreateStream(new ScheduleSemaphoreQuery(pid), ExecutionBudget.CurrentToken).FirstOrDefaultAsync(ExecutionBudget.CurrentToken);
			if (task is null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsNoTaskWithPidFormat), executor, pid);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugTaskFormat), executor, pid);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugOwnerFormat), executor, queued.Owner?.ToString() ?? "?");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugSemaphoreFormat), executor, task.SemaphoreSource);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugCommandFormat), executor, task.Command.ToPlainText());
			if (task.RunDelay.HasValue)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugDelayFormat), executor, task.RunDelay.Value.TotalSeconds.ToString("F1"));
			}

			return CallState.Empty;
		}

		AnySharpObject target;
		if (args.Count > 0)
		{
			var playerName = args["0"].Message?.ToPlainText();
			if (string.IsNullOrEmpty(playerName))
			{
				target = executor;
			}
			else
			{
				if (await LocateService.LocateAndNotifyIfInvalid(
						parser, executor, executor, playerName, LocateFlags.All) is not AnySharpObject located)
				{
					return new CallState(ErrorMessages.Returns.InvalidTarget);
				}
				target = located;
			}
		}
		else
		{
			target = executor;
		}

		if (!await PermissionService.Controls(executor, target) && !await executor.IsPriv() && !await executor.HasPower("SEE_QUEUE"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var targetDbRef = target.Object().DBRef;

		if (switches.Contains("ALL"))
		{
			if (!await executor.IsPriv() && !await executor.HasPower("SEE_QUEUE"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var allTasks = await Mediator.CreateStream(new ScheduleAllTasksQuery()).ToArrayAsync();
			// Usage is an optional extension; legacy schedulers still provide the queue listing.
			SharpMUSH.Library.Models.SchedulerModels.QueueUsage? usage;
			try { usage = queueReader.GetQueueUsage(); }
			catch (NotSupportedException) { usage = null; }
			if (usage is not null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueUsage), usage.Total, Configuration.CurrentValue.Limit.GlobalQueueLimit, Configuration.CurrentValue.Limit.PlayerQueueLimit);
				foreach (var rejection in usage.Rejections.OrderBy(x => x.Key))
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueRejections), rejection.Key, rejection.Value);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAllHeader), executor);
			foreach (var (group, tasks) in allTasks)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAllGroupFormat), executor, group, tasks.Length);
			}

			return CallState.Empty;
		}


		SharpMUSH.Library.Models.SchedulerModels.SemaphoreTaskData[] semaphoreTasks;
		try
		{
			semaphoreTasks = await Mediator.CreateStream(new ScheduleSemaphoreQuery(targetDbRef))
				.Where(async (task, ct) => await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
					.CanAccessLegacyAsync(executor, task.Pid, mutate: false, ct)).ToArrayAsync();
		}
		catch (NotSupportedException)
		{
			// A legacy scheduler cannot prove source ownership for these command bodies.
			return await QueueInspectionUnsupported(executor);
		}
		var delayTasks = await Mediator.CreateStream(new ScheduleDelayQuery(targetDbRef)).ToArrayAsync();
		// Only the command queue's size is reported, so it is counted rather than collected.
		var enqueueCount = await Mediator.CreateStream(new ScheduleEnqueueQuery(targetDbRef)).CountAsync();

		if (switches.Contains("SUMMARY"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSummaryHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), executor, enqueueCount);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), executor, delayTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), executor, semaphoreTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsLoadAverageZero), executor);
			return CallState.Empty;
		}

		if (switches.Contains("QUICK"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsQuickHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), executor, enqueueCount);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), executor, delayTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), executor, semaphoreTasks.Length);
			return CallState.Empty;
		}

		var targetName = target.Object().DBRef.ToString();
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsQueueForTargetFormat), executor, targetName);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), executor, enqueueCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), executor, delayTasks.Length);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), executor, semaphoreTasks.Length);

		if (semaphoreTasks.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreTasksHeader), executor);
			foreach (var task in semaphoreTasks.Take(10))
			{
				var delay = task.RunDelay.HasValue ? $"+{task.RunDelay.Value.TotalSeconds:F1}s" : "ready";
				var commandText = task.Command.ToPlainText();
				var truncatedCommand = commandText.Length > 40
					? commandText[..40]
					: commandText;
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreTaskEntryFormat), executor, task.Pid, task.SemaphoreSource, delay, truncatedCommand);
			}
			if (semaphoreTasks.Length > 10)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAndMoreFormat), executor, semaphoreTasks.Length - 10);
			}
		}

		if (delayTasks.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueHeader), executor);
			foreach (var pid in delayTasks.Take(10))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitTaskEntryFormat), executor, pid);
			}
			if (delayTasks.Length > 10)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAndMoreFormat), executor, delayTasks.Length - 10);
			}
		}
		IReadOnlyList<SharpMUSH.Library.Models.SchedulerModels.QueueEntrySnapshot>? entries;
		try { entries = queueReader.GetQueueEntries(); }
		catch (NotSupportedException) { entries = null; }
		if (entries is not null)
		{
			var paused = entries.Count(e => e.State == SharpMUSH.Library.Models.SchedulerModels.QueueEntryState.Paused
				&& (e.Source == targetDbRef || e.Owner == targetDbRef));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueuePausedHint), executor, paused);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@TRIGGER", Output = CommandOutput.Runs,
		Switches = ["CLEARREGS", "SPOOF", "INLINE", "NOBREAK", "LOCALIZE", "INPLACE", "MATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = int.MaxValue, ParameterNames = ["object/attribute", "arguments..."])]
	public async ValueTask<Option<CallState>> Trigger(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustSpecifyAttributePath), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var parts = attributePath.Split('/', 2);
		if (parts.Length < 2)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustSpecifyObjectAttributePath), executor);
			return new CallState(ErrorMessages.Returns.InvalidPath);
		}

		var objectName = parts[0];
		var attributeName = parts[1];

		// Locate the target object AS THE EXECUTOR (looker=executor, perm=executor). The object running
		// @trigger names and must control the target (the Controls(executor, target) check below), and
		// PennMUSH matches command arguments relative to the executor (oracle-confirmed). Passing the
		// enactor as the permission object made the looker-gate (LocateService: !Nearby && !See_All &&
		// !Controls) fail when a mortal, REMOTE enactor triggered a $-command that does @trigger %!/attr.
		// This only fixes the LOOKUP; @trigger's distinct semantics are unchanged — the attribute is still
		// QUEUED to run AS the target object (the new executor), with the triggerer as the enactor (below).
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, objectName, LocateFlags.All) switch
		{
			AnySharpObject targetObject => await TriggerAsync(parser, executor, enactor, targetObject, args, switches,
				attributeName),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> TriggerAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject enactor, AnySharpObject targetObject, Dictionary<string, CallState> args, string[] switches,
		string attributeName)
	{
		if (!await PermissionService.Controls(executor, targetObject))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerPermissionDeniedDoNotControl), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (await AttributeService.GetAttributeAsync(
				executor, targetObject, attributeName, IAttributeService.AttributeMode.Read, false)
			is not SharpAttribute[] attributeChain)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerNoSuchAttributeFormat), executor, attributeName);
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		var attribute = attributeChain.Last();
		var attributeText = attribute.Value.ToPlainText();

		// With /match, the first argument (index 1) is the test string. Refused before the notice below, so a
		// refusal is never also reported as a trigger.
		CallState? matchArg = null;
		if (switches.Contains("MATCH") && (!args.TryGetValue("1", out matchArg) || matchArg.Message == null))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustProvideMatchString), executor);
			return new CallState(ErrorMessages.Returns.NoMatchString);
		}

		// do_trigger (PennMUSH src/set.c:1341-1345): once queue_attribute_base_priv has found a readable
		// attribute, the triggerer hears "<name> - Triggered." unless AreQuiet - even if the attribute is
		// empty or /match finds nothing, since queue_attribute_useatr still returns 1. Penn queues the body,
		// so the notice comes before anything the attribute does.
		if (!await targetObject.Object().AreQuietAsync(executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerTriggeredFormat), executor,
				targetObject.Object().Name);
		}

		if (string.IsNullOrWhiteSpace(attributeText))
		{
			return CallState.Empty;
		}

		// Determine enactor for execution based on /spoof switch.
		// PennMUSH semantics (@trigger2 help):
		//   No /spoof (default): the object USING @trigger (executor) becomes the enactor (%#)
		//   /spoof: preserve the current enactor (the original player who started the chain)
		var executionEnactor = switches.Contains("SPOOF") ? enactor.Object().DBRef : executor.Object().DBRef;

		// Build argument registers from all provided arguments.
		// args["0"] is the object/attribute path (LHS); args["1"] onward are the comma-separated
		// RSArgs that become %0, %1, %2, … inside the triggered attribute.
		// These go into EnvironmentRegisters (the positional %0-%9 args), NOT the q-register stack.
		var envRegisters = new Dictionary<string, CallState>();
		for (var i = 1; i < args.Count; i++)
		{
			if (args.TryGetValue(i.ToString(), out var argValue) && argValue.Message != null)
			{
				envRegisters[(i - 1).ToString()] = argValue;
			}
		}

		// Q-registers from the calling context are copied into the triggered attribute unless
		// /clearregs is specified (PennMUSH @trigger2 help: "Q-registers set at the time @trigger
		// is run will be copied and made available in the triggered attribute").
		var registerStack = new ConcurrentStack<Dictionary<string, MString>>();
		if (switches.Contains("CLEARREGS"))
		{
			registerStack.Push(new Dictionary<string, MString>());
		}
		else
		{
			parser.CurrentState.Registers.TryPeek(out var currentRegs);
			registerStack.Push(currentRegs != null ? new Dictionary<string, MString>(currentRegs) : new());
		}

		if (switches.Contains("MATCH"))
		{
			var testString = matchArg!.Message!.ToPlainText();

			var patterns = attributeText.Split(new[] { '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);

			bool matchFound = false;
			foreach (var pattern in patterns)
			{
				var trimmedPattern = pattern.Trim();
				if (string.IsNullOrEmpty(trimmedPattern)) continue;

				var regex = SoftcodeRegex.Wildcard(trimmedPattern);

				if (SoftcodeRegex.IsMatch(regex, testString))
				{
					matchFound = true;
					break;
				}
			}

			if (!matchFound)
			{
				return CallState.Empty;
			}
		}

		// Note: INLINE switch executes immediately (current default behavior).
		// Queue dispatch available via AdmitCommandListRequest if needed for future enhancements.

		// Runs in place, so it is bounded by the in-place nesting depth (ParserState.MaxInplaceDepth),
		// not by function_recursion_limit, which PennMUSH applies to functions alone.
		var stateWithRegisters = parser.CurrentState with
		{
			Executor = targetObject.Object().DBRef,
			Enactor = executionEnactor,
			Caller = parser.CurrentState.Executor,
			Registers = registerStack,
			EnvironmentRegisters = envRegisters
		};

		var result = await parser.With(state => stateWithRegisters, newParser => newParser.WithAttributeDebug(attribute,
			async p => await p.CommandListParseVisitor(attribute.Value)()));

		return CallState.Empty with { HadErrors = result?.HadErrors == true };
	}

	/// <remarks>PennMUSH <c>cmd_allhalt</c> (<c>src/cmds.c:79</c>) is <c>do_allhalt</c>, as <c>@halt/all</c> is.</remarks>
	[SharpCommand(Name = "@ALLHALT", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^HALT",
		MinArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> AllHalt(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await HaltWorldAsync(await parser.CurrentState.KnownExecutorObject(Mediator));
}
