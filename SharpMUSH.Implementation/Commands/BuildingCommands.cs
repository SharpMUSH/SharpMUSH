using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Markup;
using System.Buffers;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@RECYCLE", Switches = ["OVERRIDE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 1,
		MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Recycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @recycle is an alias for @destroy
		return await Destroy(parser, _2);
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// NOTE: Cost parameter requires economy/quota system implementation.
	/// </remarks>
	[SharpCommand(Name = "@CREATE", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 3, ParameterNames = ["name", "cost", "dbref"])]
	public async ValueTask<Option<CallState>> Create(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await BuildingHelpers.CreateThingAsync(parser, Mediator, Database, Configuration, ValidateService,
			NotifyService, EventService, executor, parser.CurrentState.Arguments["0"].Message!) switch
		{
			DBRef thing => new CallState(thing.ToString()),
			Error<string> error => new CallState(error.Value)
		};
	}

	[SharpCommand(Name = "@FIRSTEXIT", Switches = [], Behavior = CB.Default | CB.Args, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> FirstExit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;

		foreach (var exit in args)
		{
			// NOTE: Should verify executor has CONTROL permission over the room containing the exit
			await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, exit.Value.Message!.ToPlainText(),
				LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsPreference,
				async o =>
				{
					if (o is not SharpExit oldData)
					{
						throw new InvalidOperationException("An exits-only lookup found something that is not an exit.");
					}

					var oldLocation = await oldData.Location.WithCancellation(CancellationToken.None);
					await Mediator.Send(new UnlinkExitCommand(oldData));
					await Mediator.Send(new LinkExitCommand(oldData, oldLocation));
					return CallState.Empty;
				}
			);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NAME", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.NoGuest,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "name"])]
	public async ValueTask<Option<CallState>> Rename(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var name = parser.CurrentState.Arguments["1"].Message!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, target,
			LocateFlags.All,
			async found =>
			{
				var oldName = found.Object().Name;
				var result = await ManipulateSharpObjectService.SetName(executor, found, name, true);

				// If rename was successful, trigger OBJECT`RENAME event
				// PennMUSH spec: object`rename (objid, new name, old name)
				// SetName returns a dbref on success and an "#-1 ..." error string on any failure
				// (permission denied, name/alias already in use), so gate on that prefix rather than
				// a single literal error message.
				if (result.Message?.ToPlainText().StartsWith("#-1", StringComparison.Ordinal) != true)
				{
					await EventService.TriggerEventAsync(
						parser,
						"OBJECT`RENAME",
						executor.Object().DBRef,
						found.Object().DBRef.ToString(),
						name.ToPlainText(),
						oldName);

					// real_did_it(player, thing, NULL, NULL, "ONAME", NULL, "ANAME", NOTHING, pe_regs,
					// NA_INTER_PRESENCE, AN_SYS) with %0 the old name and %1 the new (set.c:155-158).
					// There is no actor half — @name's own "Name set." is a separate notify — and the
					// loc of NOTHING resolves to the RENAMER's location, not the target's.
					await DidItService.DidIt(parser, new DidItRequest(
						Player: executor, Thing: found,
						OWhat: "ONAME", AWhat: "ANAME",
						Env0: oldName, Env1: name.ToPlainText(),
						Interact: IPermissionService.InteractType.Presence));
				}

				return result;
			}
		);
	}

	[SharpCommand(Name = "@SET", Behavior = CB.RSArgs | CB.EqSplit, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "attribute", "value"])]
	public async ValueTask<Option<CallState>> SetCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;

		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await SetHelpers.DoSet(parser, LocateService, AttributeService, ManipulateSharpObjectService,
			NotifyService, executor, args["0"].Message!, args["1"].Message!);
	}

	[SharpCommand(Name = "@DESTROY", Switches = ["OVERRIDE"], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Destroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var override_ = parser.CurrentState.Switches.Contains("OVERRIDE");

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj => await DestroyObjectAsync(parser, executor, obj, override_)
		);
	}

	/// <summary>
	/// Core destroy logic shared by <c>@destroy</c> and <c>@nuke</c>: PennMUSH <c>what_to_destroy()</c>,
	/// <c>do_destroy()</c> and <c>pre_destroy()</c>. Scheduling is reversible — nothing changes owner
	/// here; a destroyed player's probate happens when the purge actually frees them.
	/// </summary>
	private async ValueTask<CallState> DestroyObjectAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject obj,
		bool override_)
	{
		// --- Edge-case guards (PennMUSH src/destroy.c what_to_destroy) ---

		// Guests may not destroy anything.
		if (await executor.IsGuest())
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.GuestCantDestroy,
				shouldNotify: true);
		}

		// Nobody may destroy God. Every refusal below comes before anything is marked or handed over.
		if (obj.IsGod())
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.DestroyGodBlasphemous,
				shouldNotify: true);
		}

		// DESTROY_OK only means anything on a thing (PennMUSH DestOk).
		var destroyOk = obj.IsThing && await obj.HasFlag("DESTROY_OK");

		if (!await MayDestroyAsync(executor, obj, destroyOk))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		// Protect special configuration objects (player_start, master_room, base_room, default_home,
		// God, probate_judge) — PennMUSH special_object(). Shared with the destruction service so the
		// two cannot drift; a GOING special object is refused there too rather than freed.
		if (ObjectDestructionService.IsSpecialObject(obj.Object().DBRef))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.TooSpecialToDestroy,
				shouldNotify: true);
		}

		// really_safe makes SAFE absolute: even @nuke is refused until the flag is cleared.
		var reallySafe = Configuration.CurrentValue.Command.ReallySafe;
		var safe = await obj.HasFlag("SAFE");
		if (safe && !destroyOk && (reallySafe || !override_))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.SafeObject,
				notifyMessage: reallySafe
					? ErrorMessages.Notifications.SafeObjectMustUnset
					: ErrorMessages.Notifications.SafeObjectUseNuke,
				shouldNotify: true);
		}

		// "check to make sure there's no accidental destruction"
		if (!override_ && !destroyOk && !await executor.Owns(obj))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.NotYoursUseNuke,
				shouldNotify: true);
		}

		if (obj.IsThing && !override_ && await obj.HasFlag("WIZARD"))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.WizardThingUseNuke,
				shouldNotify: true);
		}

		// Player-specific guards (PennMUSH what_to_destroy, TYPE_PLAYER case)
		if (obj.IsPlayer)
		{
			if (!executor.IsPlayer)
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.ProgramsDontKillPeople,
					shouldNotify: true);
			}

			// Only a wizard can destroy a player.
			if (!await executor.IsWizard())
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.NoSuicideAllowed,
					shouldNotify: true);
			}

			// Only God can destroy another wizard.
			if (await obj.IsWizard() && !executor.IsGod())
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.EvenYouCantDoThat,
					shouldNotify: true);
			}

			// Connected players may not be destroyed.
			var isConnected = await ConnectionService
				.Get(obj.Object().DBRef)
				.AnyAsync(x => x.State == IConnectionService.ConnectionState.LoggedIn);
			if (isConnected)
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.MayNotDestroyConnectedPlayer,
					shouldNotify: true);
			}

			// Plain @destroy cannot target a player — @nuke (= override) is required.
			if (!override_)
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.MustUseNukeToDestroyPlayer,
					shouldNotify: true);
			}
		}

		// "@destroying an object while it is set GOING destroys it immediately" — PennMUSH
		// src/destroy.c do_destroy(), which calls free_object() right here. GOING_TWICE is the purge
		// cycle's own second-pass marker, not a state @destroy stops at.
		if (await obj.HasFlag("GOING"))
		{
			// Phase 2b: object-lifecycle destroy seam. Fired while the object is still in the DB so a
			// plugin hook can still read it; after FreeObjectAsync there is nothing left to read.
			await NotifyObjectDestroyingAsync(parser, obj.Object().DBRef);

			if (!await ObjectDestructionService.FreeObjectAsync(parser, obj))
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.TooSpecialToDestroy,
					shouldNotify: true);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Destroyed), executor);
			return CallState.Empty;
		}

		// Phase 2b: object-lifecycle destroy seam. The object is about to be marked GOING (scheduled for
		// destruction) but still present in the DB, so a plugin hook can read it before it is gone.
		await NotifyObjectDestroyingAsync(parser, obj.Object().DBRef);

		if (safe && !reallySafe)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SafeTargetScheduledAnyway), executor);
		}

		var destroyMsg = obj.IsPlayer
			? string.Format(ErrorMessages.Notifications.ObjectAndPossessionsScheduledDestroyedFormat, obj.Object().Name)
			: string.Format(ErrorMessages.Notifications.ObjectScheduledDestroyedFormat, obj.Object().Name);
		await NotifyService.Notify(executor, destroyMsg, executor);

		await PreDestroyAsync(parser, executor, obj.Object().DBRef, []);

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH <c>set_flag_internal</c> / <c>clear_flag_internal</c>: GOING and GOING_TWICE are
	/// wizard-only to set by hand, but scheduling and sparing an object is the server's bookkeeping, so
	/// it must not be refused for the mortal whose @destroy already passed its own permission checks.
	/// </summary>
	private async ValueTask SetFlagInternalAsync(AnySharpObject obj, string flagName, bool set)
	{
		if (await obj.HasFlag(flagName) == set
			|| await Mediator.Send(new GetObjectFlagQuery(flagName)) is not { } flag)
		{
			return;
		}

		_ = set
			? await Mediator.Send(new SetObjectFlagCommand(obj, flag))
			: await Mediator.Send(new UnsetObjectFlagCommand(obj, flag));
	}

	/// <summary>
	/// PennMUSH <c>what_to_destroy()</c>'s three routes to permission: control the object, control
	/// either end of it when it is an exit, or pass the <c>@lock/destroy</c> of a DESTROY_OK thing.
	/// </summary>
	private async ValueTask<bool> MayDestroyAsync(AnySharpObject executor, AnySharpObject obj, bool destroyOk)
	{
		if (await PermissionService.Controls(executor, obj))
		{
			return true;
		}

		if (obj is SharpExit exit)
		{
			if (await exit.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
				&& await PermissionService.Controls(executor, destination.WithExitOption()))
			{
				return true;
			}

			var source = await exit.Location.WithCancellation(CancellationToken.None);
			if (await PermissionService.Controls(executor, source.WithExitOption()))
			{
				return true;
			}
		}

		return destroyOk && await LockService.Evaluate(LockType.Destroy, obj, executor);
	}

	private async ValueTask NotifyObjectDestroyingAsync(IMUSHCodeParser parser, DBRef obj)
	{
		// Phase 2b: notify plugin IObjectLifecycleHooks that obj is about to be destroyed. No-op when no
		// dispatcher (or no hooks) is registered, so normal @destroy flow is unchanged. The object is still
		// present in the DB at the call site so a hook can read it.
		var hooks = parser.ServiceProvider.GetService<IPluginHookDispatcher>();
		if (hooks is not null)
		{
			await hooks.ObjectDestroyingAsync(obj);
		}
	}

	/// <summary>
	/// PennMUSH <c>pre_destroy()</c>: mark <paramref name="target"/> GOING, schedule what goes with it —
	/// a room's exits, and whatever a player's purge will free — and run its ADESTROY. Scheduling is
	/// reversible, so nothing changes owner here; see <see cref="UndestroyAsync"/>.
	/// </summary>
	/// <remarks>
	/// Terminates because an object is marked before anything it drags along is visited, and an
	/// already-GOING object is not revisited; <paramref name="visited"/> makes that hold even against a
	/// flag read that lags the write.
	/// </remarks>
	private async ValueTask PreDestroyAsync(IMUSHCodeParser parser, AnySharpObject executor, DBRef target,
		HashSet<int> visited)
	{
		if (!visited.Add(target.Number)
			|| await Mediator.Send(new GetObjectNodeQuery(target)) is not AnySharpObject thing
			|| await thing.HasFlag("GOING"))
		{
			return;
		}

		await SetFlagInternalAsync(thing, "GOING", true);
		await SetFlagInternalAsync(thing, "GOING_TWICE", false);

		var dragged = thing switch
		{
			SharpRoom room => await ExitsOfAsync(room),
			SharpPlayer player => await DoomedPossessionsAsync(player),
			_ => []
		};

		foreach (var next in dragged)
		{
			await PreDestroyAsync(parser, executor, next, visited);
		}

		await RunAdestroyAsync(parser, executor, thing);
	}

	/// <summary>
	/// PennMUSH <c>undestroy()</c>: spare <paramref name="target"/> and everything its survival
	/// requires — its GOING owner, an exit's source room — plus what was scheduled only on its account:
	/// a room's exits and a player's possessions. The exceptions are Penn's "two votes" compromise
	/// (<c>src/destroy.c:176-196</c>): an exit still doomed by something else stays GOING.
	/// </summary>
	/// <returns><see langword="false"/> when <paramref name="target"/> was not GOING.</returns>
	private async ValueTask<bool> UndestroyAsync(IMUSHCodeParser parser, DBRef target, HashSet<int> visited)
	{
		if (!visited.Add(target.Number)
			|| await Mediator.Send(new GetObjectNodeQuery(target)) is not AnySharpObject thing
			|| !await thing.HasFlag("GOING"))
		{
			return false;
		}

		await SetFlagInternalAsync(thing, "GOING", false);
		await SetFlagInternalAsync(thing, "GOING_TWICE", false);

		if (!await thing.HasFlag("HALT"))
		{
			await RunStartupAsync(parser, thing);
		}

		var owner = await thing.Object().Owner.WithCancellation(CancellationToken.None);
		await UndestroyAsync(parser, owner.Object.DBRef, visited);

		var destroyPossessions = Configuration.CurrentValue.Command.DestroyPossessions;

		switch (thing)
		{
			case SharpPlayer player when destroyPossessions:
				foreach (var owned in await OwnedByAsync(player))
				{
					if (owned is SharpExit exit && await IsInSomeoneElsesGoingSourceAsync(exit, player)) continue;
					await UndestroyAsync(parser, owned.Object().DBRef, visited);
				}

				break;

			case SharpExit exit:
				var source = (await exit.Location.WithCancellation(CancellationToken.None)).WithExitOption();
				await UndestroyAsync(parser, source.Object().DBRef, visited);
				break;

			case SharpRoom room:
				foreach (var exitRef in await ExitsOfAsync(room))
				{
					if (destroyPossessions && await IsDoomedByItsOwnerAsync(exitRef)) continue;
					await UndestroyAsync(parser, exitRef, visited);
				}

				break;
		}

		return true;
	}

	/// <summary>Penn: <c>IsExit(tmp) &amp;&amp; !Owns(thing, Source(tmp)) &amp;&amp; Going(Source(tmp))</c>.</summary>
	private static async ValueTask<bool> IsInSomeoneElsesGoingSourceAsync(SharpExit exit, SharpPlayer player)
	{
		var source = (await exit.Location.WithCancellation(CancellationToken.None)).WithExitOption();
		var sourceOwner = await source.Object().Owner.WithCancellation(CancellationToken.None);
		return sourceOwner.Object.DBRef.Number != player.Object.DBRef.Number && await source.HasFlag("GOING");
	}

	/// <summary>Penn: <c>Going(Owner(tmp)) &amp;&amp; !Safe(tmp)</c> — the owner's purge still frees it.</summary>
	private async ValueTask<bool> IsDoomedByItsOwnerAsync(DBRef exitRef)
	{
		if (await Mediator.Send(new GetObjectNodeQuery(exitRef)) is not AnySharpObject exit)
		{
			return false;
		}

		var owner = await exit.Object().Owner.WithCancellation(CancellationToken.None);
		return await new AnySharpObject(owner).HasFlag("GOING") && !await exit.HasFlag("SAFE");
	}

	private async ValueTask<List<DBRef>> ExitsOfAsync(SharpRoom room)
		=> await Mediator.CreateStream(new GetExitsQuery(room.Object.DBRef))
			.Select(exit => exit.Object.DBRef)
			.ToListAsync();

	/// <summary>Everything <paramref name="player"/> owns except themselves, read in full before any write.</summary>
	/// <remarks>
	/// The owner predicate is pushed down, and each result's owner is read again: a provider that ignored
	/// the predicate would otherwise have scheduling one player mark the whole database.
	/// </remarks>
	private async ValueTask<List<AnySharpObject>> OwnedByAsync(SharpPlayer player)
	{
		var playerNumber = player.Object.DBRef.Number;
		return await Mediator
			.CreateStream(new GetFilteredObjectsQuery(new ObjectSearchFilter { Owner = player.Object.DBRef }))
			.Where(candidate => candidate.DBRef.Number != playerNumber)
			.Select(async (candidate, token) => await Mediator.Send(new GetObjectNodeQuery(candidate.DBRef), token) is AnySharpObject found
				&& (await found.Object().Owner.WithCancellation(token)).Object.DBRef.Number == playerNumber
					? found
					: null)
			.Where(owned => owned is not null)
			.Select(owned => owned!)
			.ToListAsync();
	}

	/// <summary>
	/// What <c>clear_player()</c> will free when <paramref name="player"/> is purged, and so what
	/// scheduling them schedules too.
	/// </summary>
	/// <remarks>
	/// DEVIATION: Penn's filter reads <c>!Safe(thing)</c> with <c>thing</c> the player, so under
	/// <c>really_safe</c> a SAFE possession is marked and then freed by its own purge pass. Its comment
	/// states the intent this follows instead: mark exactly what <c>clear_player()</c> would free.
	/// </remarks>
	private async ValueTask<List<DBRef>> DoomedPossessionsAsync(SharpPlayer player)
	{
		var config = Configuration.CurrentValue.Command;
		if (!config.DestroyPossessions)
		{
			return [];
		}

		var doomed = new List<DBRef>();
		foreach (var owned in await OwnedByAsync(player))
		{
			var ownedDbRef = owned.Object().DBRef;
			if (ObjectDestructionService.IsSpecialObject(ownedDbRef)
				|| (config.ReallySafe && await owned.HasFlag("SAFE"))) continue;

			doomed.Add(ownedDbRef);
		}

		return doomed;
	}

	/// <summary>
	/// PennMUSH <c>pre_destroy()</c>'s <c>did_it(player, thing, …, "ADESTROY", …)</c>, when the
	/// <c>adestroy</c> option is on: the action is queued with the object as executor and the destroyer
	/// as enactor, and is looked up through parents.
	/// </summary>
	private async ValueTask RunAdestroyAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject thing)
	{
		if (!Configuration.CurrentValue.Attribute.ADestroy)
		{
			return;
		}

		await DidItService.DidIt(parser, new DidItRequest(executor, thing, AWhat: "ADESTROY"));
	}

	/// <summary>
	/// PennMUSH <c>undestroy()</c>'s <c>queue_attribute_noparent(thing, "STARTUP", thing)</c>: the
	/// object's own STARTUP, never a parent's, queued as a fresh command list with the object as both
	/// executor and enactor. The caller skips HALTed objects.
	/// </summary>
	private async ValueTask RunStartupAsync(IMUSHCodeParser parser, AnySharpObject thing)
	{
		// did_it looks the action up through parents; an object's own attribute shadows any inherited
		// one, so requiring the object's own first gives queue_attribute_noparent's lookup.
		if (await AttributeService.GetAttributeAsync(thing, thing, "STARTUP",
				IAttributeService.AttributeMode.Execute, parent: false) is not SharpAttribute[] { Length: > 0 })
		{
			return;
		}

		await DidItService.DidIt(parser, new DidItRequest(thing, thing, AWhat: "STARTUP"));
	}

	[SharpCommand(Name = "@LINK", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2, ParameterNames = ["object", "destination"])]
	public async ValueTask<Option<CallState>> Link(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var exitName = args["0"].Message!.ToPlainText();
		var destName = args["1"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, exitName, LocateFlags.All,
			async exitObj =>
			{
				if (!await PermissionService.Controls(executor, exitObj))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (exitObj is SharpExit exit)
				{
					if (destName.Equals(LinkTypeHome, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService.SetAttributeAsync(executor, exitObj, AttrLinkType, MarkupText.Plain(LinkTypeHome));
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedToHome), executor);
						return CallState.Empty;
					}
					else if (destName.Equals(LinkTypeVariable, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService.SetAttributeAsync(executor, exitObj, AttrLinkType, MarkupText.Plain(LinkTypeVariable));
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedToVariable), executor);
						return CallState.Empty;
					}

					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							// An exit may lead to any container — room, player or thing (PennMUSH can_link_to).
							// Only another exit is not a place you can end up.
							if (!destObj.IsContainer)
							{
								return await NotifyService.NotifyAndReturn(
										executor.Object().DBRef,
										errorReturn: ErrorMessages.Returns.InvalidDestination,
										notifyMessage: ErrorMessages.Notifications.InvalidDestinationExit,
										shouldNotify: true);
							}

							var destination = destObj.AsContainer;

							if (!await CanLinkTo(executor, destObj))
							{
								return await NotifyService.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.PermissionDenied,
									notifyMessage: ErrorMessages.Notifications.CantLinkToThat,
									shouldNotify: true);
							}

							var exitOwner = await exitObj.Object().Owner.WithCancellation(CancellationToken.None);
							var executorObj = executor.Object();
							var executorOwner = await executorObj.Owner.WithCancellation(CancellationToken.None);

							var exitNotControlled = !await PermissionService.Controls(executor, exitObj);
							var isOwnedByOther = exitOwner.Object.Id != executorOwner.Object.Id;

							// When linking an exit owned by someone else that executor doesn't control:
							// Check @lock/link, transfer ownership, and set HALT flag
							if (isOwnedByOther && exitNotControlled)
							{
								var linkLockPasses = await LockService.Evaluate(LockType.Link, exitObj, executor);
								if (!linkLockPasses)
								{
									return await NotifyService.NotifyAndReturn(
										executor.Object().DBRef,
										errorReturn: ErrorMessages.Returns.PermissionDenied,
										notifyMessage: ErrorMessages.Notifications.DontPassLinkLock,
										shouldNotify: true);
								}

								if (executor is SharpPlayer executorPlayer)
								{
									try
									{
										await Mediator.Send(new SetObjectOwnerCommand(exitObj, executorPlayer));
									}
									catch (Exception)
									{
										return await NotifyService.NotifyAndReturn(
											executor.Object().DBRef,
											errorReturn: ErrorMessages.Returns.PermissionDenied,
											notifyMessage: ErrorMessages.Notifications.FailedToTransferOwnership,
											shouldNotify: true);
									}
								}

								// Set HALT flag to prevent looping
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, exitObj, "HALT", true);
							}

							await AttributeService.SetAttributeAsync(executor, exitObj, AttrLinkType, MarkupText.Empty);

							await Mediator.Send(new LinkExitCommand(exit, destination));

							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedExitToRoom), executor, exitObj.Object().DBRef.Number, destination.Object().DBRef.Number);
							return CallState.Empty;
						}
					);
				}
				else if (exitObj.IsThing || exitObj.IsPlayer)
				{
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							// create.c:395: a home is any object that is not an exit — a room, a player or a
							// thing. safe_tel's "homed to the mover" case (move.c:311) is only reachable
							// because a player can be a home.
							if (!destObj.IsContainer)
							{
								return await NotifyService.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.InvalidDestination,
									notifyMessage: ErrorMessages.Notifications.HomeIsAnExit,
									shouldNotify: true);
							}

							// create.c:399.
							if (destObj.Object().DBRef.Equals(exitObj.Object().DBRef))
							{
								return await NotifyService.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.InvalidDestination,
									notifyMessage: ErrorMessages.Notifications.CannotLinkToItself,
									shouldNotify: true);
							}

							// create.c:404. Penn's following room == HOME guard (create.c:412) is
							// unreachable: this branch matches with MAT_EVERYTHING, which has no
							// home entry, and only parse_linkable_room ever yields HOME.
							if (!await CanSetHomeTo(executor, destObj))
							{
								return await NotifyService.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.PermissionDenied,
									notifyMessage: ErrorMessages.Notifications.PermissionDenied,
									shouldNotify: true);
							}

							// Convert to AnySharpContent for SetObjectHomeCommand
							var contentObj = exitObj.AsContent;
							await Mediator.Send(new SetObjectHomeCommand(contentObj, destObj.AsContainer));
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HomeSet), executor);
							return CallState.Empty;
						}
					);
				}
				else if (exitObj is SharpRoom room)
				{
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (destObj is not SharpRoom destinationRoom)
							{
								return await NotifyService.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.InvalidDestination,
									notifyMessage: ErrorMessages.Notifications.DropToMustBeRoom,
									shouldNotify: true);
							}

							await Mediator.Send(new LinkRoomCommand(room, destinationRoom));
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DropToSet), executor);
							return CallState.Empty;
						}
					);
				}

				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.InvalidObjectType,
					notifyMessage: ErrorMessages.Notifications.InvalidObjectTypeForLinking,
					shouldNotify: true);
			}
		);
	}

	[SharpCommand(Name = "@NUKE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Nuke(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// @nuke is @destroy/override: it bypasses the SAFE flag and the "use @nuke" player guard.
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj => await DestroyObjectAsync(parser, executor, obj, override_: true)
		);
	}

	[SharpCommand(Name = "@UNDESTROY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnDestroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService.Controls(executor, obj))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (!await UndestroyAsync(parser, obj.Object().DBRef, []))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.NotGoing,
						notifyMessage: ErrorMessages.Notifications.NotMarkedForDestruction,
						shouldNotify: true);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SparedFromDestructionFormat), executor, obj.Object().Name);

				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@DIG", Switches = ["TELEPORT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 6, ParameterNames = ["name", "exits"])]
	public async ValueTask<Option<CallState>> Dig(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// NOTE: We discard arguments 4-6.
		var executorBase = await parser.CurrentState.KnownExecutorObject(Mediator);
		var executor = executorBase.Object();
		var roomName = parser.CurrentState.Arguments["0"].Message!;
		parser.CurrentState.Arguments.TryGetValue("1", out var exitToCallState);
		parser.CurrentState.Arguments.TryGetValue("2", out var exitFromCallState);
		var exitTo = exitToCallState?.Message;
		var exitFrom = exitFromCallState?.Message;

		if (string.IsNullOrWhiteSpace(parser.CurrentState.Arguments["0"].Message!.ToPlainText()))
		{
			await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.DigWhat), executorBase);
			return new CallState(ErrorMessages.Returns.NoRoomNameSpecified);
		}

		// NOTE: Additional permission checks needed:
		// - Can executor create rooms (quota check)
		// - Does executor have DIG permission

		var response = await Mediator.Send(new CreateRoomCommand(roomName.ToPlainText(),
			await executor.Owner.WithCancellation(CancellationToken.None)));
		await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.RoomCreatedWithNumberFormat), executorBase, roomName, response.Number);

		var creatorZone = await executor.Zone.WithCancellation(CancellationToken.None);
		if (creatorZone is AnySharpObject zone)
		{
			if (await Mediator.Send(new GetObjectNodeQuery(response)) is AnySharpObject newRoom)
			{
				// Check for cycles before inheriting zone from creator
				if (await HelperFunctions.SafeToAddZone(Mediator, Database, newRoom, zone))
				{
					await Mediator.Send(new SetObjectZoneCommand(newRoom, zone));
				}
			}
		}

		if (!string.IsNullOrWhiteSpace(exitTo?.ToPlainText()))
		{
			var exitToName = exitTo.ToPlainText().Split(";");
			// CAN CREATE EXIT HERE?
			// CAN LINK TO DESTINATION?

			var toExitResponse = await Mediator.Send(new CreateExitCommand(exitToName.First(),
				exitToName.Skip(1).ToArray(), await executorBase.Where(),
				await executor.Owner.WithCancellation(CancellationToken.None)));
			await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.OpenedExit), executorBase, $"#{toExitResponse.Number}");
			await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.TryingToLink), executorBase);

			if (await Mediator.Send(new GetObjectNodeQuery(response)) is not (AnySharpObject and SharpRoom newRoomObject)
					|| await Mediator.Send(new GetObjectNodeQuery(toExitResponse)) is not (AnySharpObject and SharpExit newExitObject))
			{
				throw new InvalidOperationException("The room and exit just dug must exist.");
			}

			await Mediator.Send(new LinkExitCommand(newExitObject, newRoomObject));

			await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.LinkedExitToRoom), executorBase, toExitResponse.Number, response.Number);
		}

		if (!string.IsNullOrWhiteSpace(exitFrom?.ToPlainText()))
		{
			// CAN CREATE EXIT THERE?
			// CAN LINK BACK TO CURRENT ROOM?

			var exitFromName = exitFrom.ToPlainText().Split(";");
			if (await Mediator.Send(new GetObjectNodeQuery(response)) is not (AnySharpObject and SharpRoom newRoomObject))
			{
				throw new InvalidOperationException("The room just dug must exist.");
			}

			var fromExitResponse = await Mediator.Send(new CreateExitCommand(exitFromName.First(),
				exitFromName.Skip(1).ToArray(), newRoomObject,
				await executor.Owner.WithCancellation(CancellationToken.None)));
			if (await Mediator.Send(new GetObjectNodeQuery(fromExitResponse)) is not (AnySharpObject and SharpExit newExitObject))
			{
				throw new InvalidOperationException("The exit just opened must exist.");
			}

			await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.OpenedExit), executorBase, $"#{fromExitResponse.Number}");
			await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.TryingToLink), executorBase);

			var where = await executorBase.Where();
			await Mediator.Send(new LinkExitCommand(newExitObject, where));

			await NotifyService.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.LinkedExitToRoom), executorBase, fromExitResponse.Number, where.Object().DBRef.Number);
		}

		return new CallState(response.ToString());
	}

	private ValueTask<bool> CanLinkTo(AnySharpObject executor, AnySharpObject destination)
		=> PermissionService.CanLinkToAsync(executor, destination);

	/// <summary>
	/// PennMUSH <c>do_link</c>'s home gate (<c>src/create.c:404</c>): <c>!controls(player, room) &amp;&amp;
	/// !Abode(room)</c>. Any non-exit can be a home, so without this a player could home an object
	/// they control into someone else's inventory. <c>ABODE</c> is ROOM-only in the flag seed, as in
	/// PennMUSH, so a player or thing destination is gated on control alone.
	/// </summary>
	private async ValueTask<bool> CanSetHomeTo(AnySharpObject executor, AnySharpObject destination)
	{
		if (await PermissionService.Controls(executor, destination))
		{
			return true;
		}

		return await destination.HasFlag("ABODE");
	}

	[SharpCommand(Name = "@OPEN", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 5, ParameterNames = ["exit", "destination"])]
	public async ValueTask<Option<CallState>> Open(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var exitName = args["0"].Message!.ToPlainText();

		var exitParts = exitName.Split(";");
		var primaryName = exitParts[0];
		var aliases = exitParts.Skip(1).ToArray();

		var sourceRoom = await executor.Where();
		if (args.ContainsKey("2") && !string.IsNullOrWhiteSpace(args["2"].Message!.ToPlainText()))
		{
			var sourceRoomName = args["2"].Message!.ToPlainText();
			if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor, executor, sourceRoomName, LocateFlags.All) is not (AnySharpObject and SharpRoom namedRoom))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SourceMustBeARoom), executor);
				return new CallState(ErrorMessages.Returns.NotARoom);
			}
			sourceRoom = namedRoom;
		}

		if (!await PermissionService.Controls(executor, sourceRoom.WithExitOption()))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		var exitDbRef = await Mediator.Send(new CreateExitCommand(
			primaryName,
			aliases,
			sourceRoom,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)
		));

		var creatorZone = await executor.Object().Zone.WithCancellation(CancellationToken.None);
		if (creatorZone is AnySharpObject zone)
		{
			if (await Mediator.Send(new GetObjectNodeQuery(exitDbRef)) is AnySharpObject newExit)
			{
				// Check for cycles before inheriting zone from creator
				if (await HelperFunctions.SafeToAddZone(Mediator, Database, newExit, zone))
				{
					await Mediator.Send(new SetObjectZoneCommand(newExit, zone));
				}
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OpenedExit), executor, $"#{exitDbRef.Number}");

		if (args.ContainsKey("1") && !string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
		{
			var destName = args["1"].Message!.ToPlainText();
			if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor, executor, destName, LocateFlags.All) is not AnySharpObject destination)
			{
				// LocateAndNotifyIfInvalidWithCallState has already said why.
				return new CallState(exitDbRef.ToString());
			}

			// An exit may lead to any container — room, player or thing (PennMUSH can_link_to). Anything
			// else, or anywhere the executor may not link into, is reported rather than leaving the exit
			// silently unlinked.
			if (!destination.IsContainer
					|| !await CanLinkTo(executor, destination))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantLinkToThat), executor);
				return new CallState(exitDbRef.ToString());
			}

			if (await Mediator.Send(new GetObjectNodeQuery(exitDbRef)) is not (AnySharpObject and SharpExit exitObj))
			{
				throw new InvalidOperationException("The exit just opened must exist.");
			}

			await Mediator.Send(new LinkExitCommand(exitObj, destination.AsContainer));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedToNameFormat), executor, destName);
		}

		return new CallState(exitDbRef.ToString());
	}

	[SharpCommand(Name = "@CLONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "name", "cost"])]
	public async ValueTask<Option<CallState>> Clone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		var defaultHome = Configuration.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		if (await Mediator.Send(new GetObjectNodeQuery(defaultHomeDbref)) is not AnySharpObject location
				|| location.IsExit)
		{
			return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.NotARoom,
					notifyMessage: ErrorMessages.Notifications.DefaultHomeLocationInvalid,
					shouldNotify: true);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService.Controls(executor, obj))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (obj.IsPlayer)
				{
					return await NotifyService.NotifyAndReturn(
							executor.Object().DBRef,
							errorReturn: ErrorMessages.Returns.InvalidObjectType,
							notifyMessage: ErrorMessages.Notifications.CannotClonePlayers,
							shouldNotify: true);
				}

				var newName = obj.Object().Name;
				if (args.ContainsKey("1") && !string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
				{
					newName = args["1"].Message!.ToPlainText();
				}

				DBRef cloneDbRef;
				var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

				if (obj.IsThing)
				{
					cloneDbRef = await Mediator.Send(new CreateThingCommand(
						newName,
						await executor.Where(),
						owner,
						location.AsContainer
					));
				}
				else if (obj.IsRoom)
				{
					cloneDbRef = await Mediator.Send(new CreateRoomCommand(
						newName,
						owner
					));
				}
				else if (obj.IsExit)
				{
					var nameParts = newName.Split(";");
					cloneDbRef = await Mediator.Send(new CreateExitCommand(
						nameParts[0],
						nameParts.Skip(1).ToArray(),
						await executor.Where(),
						owner
					));
				}
				else
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.InvalidObjectType,
						notifyMessage: ErrorMessages.Notifications.CannotCloneThisObjectType,
						shouldNotify: true);
				}

				if (await Mediator.Send(new GetObjectNodeQuery(cloneDbRef)) is not AnySharpObject clonedObj)
				{
					throw new InvalidOperationException("The clone just created must exist.");
				}

				// Penn's atr_cpy (attrib.c:1692-1710) walks the source's flat, sorted attribute
				// list - branch vs. leaf is purely a naming convention over one namespace - and
				// for each attribute checks AF_Nocopy, then calls atr_new_add(..., makeroots:
				// false). With makeroots false, atr_new_add (attrib.c:756-820) silently aborts
				// without adding when the immediate parent isn't already on the destination
				// (:804-806). Because the list is sorted with parent before child, a no_clone
				// BRANCH is itself skipped by atr_cpy, and its leaves then find no parent on the
				// clone either and are dropped too - incidentally, via the missing-root abort,
				// not via any permission walk of their own. GetAttributesByRegexAsync (via
				// GetAttributesQuery in Regex mode) is used here rather than the depth-1
				// enumeration above (or the unsorted GetAttributesAsync) because it walks the
				// whole tree and sorts LongName ascending - parent before child - which this
				// skip-propagation depends on.
				var skippedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				await foreach (var sourceAttribute in Mediator.CreateStream(
					new GetAttributesQuery(obj.Object().DBRef, ".*", false,
						IAttributeService.AttributePatternMode.Regex)))
				{
					var attr = sourceAttribute.Attribute;
					var longName = attr.LongName!;
					var attrPath = longName.Split('`');
					var lastSeparator = longName.LastIndexOf('`');
					var parentLongName = lastSeparator < 0 ? null : longName[..lastSeparator];
					var parentSkipped = parentLongName is not null && skippedAttributes.Contains(parentLongName);

					// The "_"-prefix skip is a pre-existing SharpMUSH-only filter, orthogonal to
					// Penn's no_clone. It folds into the same skip set so that a "_"-prefixed
					// branch's children don't get silently auto-vivified a stripped-down parent
					// missing-root hazard the no_clone propagation above exists to avoid.
					if (attr.IsNoCopy() || attr.Name.StartsWith("_") || parentSkipped)
					{
						skippedAttributes.Add(longName);
						continue;
					}

					// AL_CREATOR(ptr) is passed through unchanged in atr_cpy (attrib.c:1706) - a
					// cloned attribute keeps its original creator, not the cloner.
					var creator = await attr.Owner.WithCancellation(CancellationToken.None) ?? owner;
					var setResult = await AttributeService.SetAttributeAsync(executor, clonedObj, longName, attr.Value, creator);

					// A failed set means the branch was NOT actually copied. Treating it as
					// skipped keeps the invariant this whole loop depends on: a LongName only
					// avoids the skip set if it genuinely landed on the clone. Without this, a
					// child under a branch that failed to set would still see its parent as
					// "not skipped" and auto-vivify a stripped-down stand-in via
					// 608-675) - the exact hazard this propagation exists to prevent. Unreachable
					// today (the clone's owner always controls the freshly-created destination),
					// but one permission change away from live.
					if (setResult is Error<string>)
					{
						skippedAttributes.Add(longName);
						continue;
					}

					// AL_FLAGS(ptr) is assigned directly alongside AL_CREATOR on the very same
					// atr_new_add call (attrib.c:1706-1707) - Penn copies the flags too, with no
					// permission gate at all: atr_new_add is a deliberately "dangerous", bypass-
					// everything helper reserved for database load and atr_cpy (its own doc
					// comment, attrib.c:750-754). SetAttributeAsync only just created the
					// destination attribute with whatever SharpAttributeEntry.DefaultFlags
					// applies (AttributeService.cs, applied inside SetAttributeCommand's handler)
					// - a SharpMUSH-only mechanism Penn has no equivalent of - so the destination
					// flag set is forced to match the source's exactly, mirroring Penn's
					// unconditional overwrite rather than a union. Goes straight through
					// SetAttributeFlagCommand/UnsetAttributeFlagCommand (no permission checks in
					// either handler) rather than AttributeService.SetAttributeFlagsAsync, for the
					// same bypass reason atr_new_add itself bypasses can_write_attr.
					var destAttribute = await Mediator.CreateStream(new GetAttributeQuery(clonedObj.Object().DBRef, attrPath))
						.LastOrDefaultAsync();

					if (destAttribute is not null)
					{
						var sourceFlagNames = attr.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
						var destFlagNames = destAttribute.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

						foreach (var flag in destAttribute.Flags.Where(f => !sourceFlagNames.Contains(f.Name)))
						{
							await Mediator.Send(new UnsetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
						}

						foreach (var flag in attr.Flags.Where(f => !destFlagNames.Contains(f.Name)))
						{
							await Mediator.Send(new SetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
						}
					}
					else
					{
						// SetAttributeAsync above reported success, so the destination attribute
						// should exist - this re-fetch failing is not the "copy failed" case
						// handled above (that one still owns skippedAttributes so children don't
						// auto-vivify a stripped parent). Here the value genuinely landed; only
						// the flag sync had nothing to attach to. Surface it instead of silently
						// leaving the clone's flags at SetAttributeAsync's defaults.
						Logger?.LogWarning(
							"Clone flag sync skipped for {LongName} on {CloneDbRef}: destination attribute was not found immediately after a successful set",
							longName, clonedObj.Object().DBRef);
					}
				}

				foreach (var (name, data) in obj.Object().Locks)
				{
					if (data.Flags.HasFlag(Library.Services.LockService.LockFlags.NoClone)) continue;
					var copied = await Mediator.Send(new CopyLockCommand(obj.Object(), clonedObj.Object(), name, executor));
					if (copied is Error<string> failure) await NotifyService.Notify(executor, $"Unable to clone {name} lock: {failure.Value}", executor);
				}

				// Synchronised to the source, not unioned with it. The clone is created through the same
				// path as any other object and therefore arrives carrying the configured creation
				// defaults, so copying only what the source has would leave a NO_COMMAND that the source
				// had deliberately cleared — and the $-commands just copied onto the clone would not run.
				// The attribute-flag sync above works the same way, for the same reason.
				var copyable = await obj.Object().Flags.Value
					.Where(flag => preserve || (!flag.Name.Contains("WIZARD") && !flag.Name.Contains("ROYALTY")))
					.Select(flag => flag.Name)
					.ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

				// The provider reads the flags in full when enumeration starts, and an unset swaps the
				// object's Flags for a new list rather than editing the one being walked.
				await foreach (var flag in clonedObj.Object().Flags.Value.Where(flag => !copyable.Contains(flag.Name)))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, clonedObj, $"!{flag.Name}", false);
				}

				foreach (var flagName in copyable)
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, clonedObj, flagName, false);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ClonedNewObjectFormat), executor, cloneDbRef.Number);
				return new CallState(cloneDbRef.ToString());
			}
		);
	}

	[SharpCommand(Name = "@MONIKER", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "moniker"])]
	public async ValueTask<Option<CallState>> Moniker(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService.Controls(executor, obj))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (!args.ContainsKey("1") || string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
				{
					await AttributeService.SetAttributeAsync(executor, obj, "MONIKER", MarkupText.Plain(""));
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MonikerCleared), executor);
					return CallState.Empty;
				}

				var moniker = args["1"].Message!;
				await AttributeService.SetAttributeAsync(executor, obj, "MONIKER", moniker);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MonikerSet), executor);
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@PARENT", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "parent"])]
	public async ValueTask<Option<CallState>> Parent(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!await PermissionService.Controls(executor, target))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				switch (args)
				{
					case { Count: 1 }:
					case { Count: 2 } when args["1"].Message!.ToPlainText()
						.Equals("none", StringComparison.InvariantCultureIgnoreCase):

						return await ManipulateSharpObjectService.UnsetParent(executor, target, true);
					default:

						return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
							parser, executor, executor,
							args["1"].Message!.ToPlainText(), LocateFlags.All,
							async newParent
								=> await ManipulateSharpObjectService.SetParent(executor, target, newParent, true));
				}
			}
		);
	}


	[SharpCommand(Name = "@UNLINK", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Unlink(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService.Controls(executor, obj))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (obj is SharpExit exit)
				{
					await AttributeService.SetAttributeAsync(executor, obj, AttrLinkType, MarkupText.Empty);

					await Mediator.Send(new UnlinkExitCommand(exit));
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.UnlinkedExit), executor, obj.Object().DBRef.Number);
					return CallState.Empty;
				}
				else if (obj is SharpRoom room)
				{
					await Mediator.Send(new UnlinkRoomCommand(room));
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DropToRemoved), executor);
					return CallState.Empty;
				}

				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.InvalidObjectType,
					notifyMessage: ErrorMessages.Notifications.InvalidObjectTypeGeneric,
					shouldNotify: true);
			}
		);
	}

	/// <summary>
	/// PennMUSH maps @UNRECYCLE onto cmd_undestroy (src/command.c:324), the same handler @UNDESTROY
	/// gets at :319 — the two are one command under two names. <see cref="SharpCommandAttribute"/>
	/// carries no alias field, so the alias is a delegation.
	/// </summary>
	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await UnDestroy(parser, _2);
}