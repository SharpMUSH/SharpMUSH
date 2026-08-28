using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OneOf.Types;
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
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@RECYCLE", Switches = ["OVERRIDE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 1,
		MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Recycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @recycle is an alias for @destroy
		return await Destroy(parser, _2);
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// NOTE: Cost parameter requires economy/quota system implementation.
	/// </remarks>
	[SharpCommand(Name = "@CREATE", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 3, ParameterNames = ["name", "cost", "dbref"])]
	public static async ValueTask<Option<CallState>> Create(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var args = parser.CurrentState.Arguments;
		var name = args["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var location = await Mediator!.Send(new GetObjectNodeQuery(defaultHomeDbref));

		if (location.IsNone || location.IsExit)
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.NotARoom,
				notifyMessage: ErrorMessages.Notifications.DefaultHomeLocationInvalid,
				shouldNotify: true);
		}

		if (!await ValidateService!.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.BadObjectName,
				notifyMessage: ErrorMessages.Notifications.InvalidNameThing,
				shouldNotify: true);
		}

		var thing = await Mediator!.Send(new CreateThingCommand(name.ToPlainText(),
			executor.AsContainer,
			await executor.Object().Owner.WithCancellation(CancellationToken.None),
			location.Known.AsContainer));

		var creatorZone = await executor.Object().Zone.WithCancellation(CancellationToken.None);
		if (!creatorZone.IsNone)
		{
			var newThing = await Mediator.Send(new GetObjectNodeQuery(thing));
			if (!newThing.IsNone)
			{
				// Check for cycles before inheriting zone from creator
				if (await HelperFunctions.SafeToAddZone(Mediator, Database!, newThing.Known, creatorZone.Known))
				{
					await Mediator.Send(new SetObjectZoneCommand(newThing.Known, creatorZone.Known));
				}
			}
		}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Created), executor, name, thing);

		await EventService!.TriggerEventAsync(
			parser,
			"OBJECT`CREATE",
			executor.Object().DBRef,
			thing.ToString(),
			""); // null for cloned-from (not a clone)

		// Phase 2b: C# object-lifecycle hooks fire alongside the softcode OBJECT`CREATE event.
		var createHooks = parser.ServiceProvider.GetService<IPluginHookDispatcher>();
		if (createHooks is not null)
		{
			await createHooks.ObjectCreatedAsync(thing, executor.Object().DBRef);
		}

		return new CallState(thing.ToString());
	}

	[SharpCommand(Name = "@FIRSTEXIT", Switches = [], Behavior = CB.Default | CB.Args, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> FirstExit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;

		await foreach (var exit in args.ToAsyncEnumerable())
		{
			// NOTE: Should verify executor has CONTROL permission over the room containing the exit
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, exit.Value.Message!.ToPlainText(),
				LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsPreference,
				async o =>
				{
					var oldData = o.AsExit;
					var oldLocation = await oldData.Location.WithCancellation(CancellationToken.None);
					await Mediator!.Send(new UnlinkExitCommand(oldData));
					await Mediator.Send(new LinkExitCommand(oldData, oldLocation));
					return CallState.Empty;
				}
			);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NAME", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.NoGuest,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "name"])]
	public static async ValueTask<Option<CallState>> Rename(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var target = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var name = parser.CurrentState.Arguments["1"].Message!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, target,
			LocateFlags.All,
			async found =>
			{
				var oldName = found.Object().Name;
				var result = await ManipulateSharpObjectService!.SetName(executor, found, name, true);

				// If rename was successful, trigger OBJECT`RENAME event
				// PennMUSH spec: object`rename (objid, new name, old name)
				if (result.ToString() != ErrorMessages.Returns.PermissionDenied)
				{
					await EventService!.TriggerEventAsync(
						parser,
						"OBJECT`RENAME",
						executor.Object().DBRef,
						found.Object().DBRef.ToString(),
						name.ToPlainText(),
						oldName);
				}

				return result;
			}
		);
	}

	[SharpCommand(Name = "@SET", Behavior = CB.RSArgs | CB.EqSplit, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "attribute", "value"])]
	public static async ValueTask<Option<CallState>> SetCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(args["0"].Message!));
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();

		if (!split.TryPickT0(out var details, out _))
		{
			return new CallState(ErrorMessages.Returns.BadArgumentFormatToSet);
		}

		var (dbref, maybeAttribute) = details;

		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			enactor,
			executor,
			dbref,
			LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var realLocated = locate.AsSharpObject;

		if (!string.IsNullOrEmpty(maybeAttribute))
		{
			// Every token is applied as ONE batch (Task 6 fix round 1, M2): Penn's
			// do_attrib_flags/af_helper checks permission once for the whole flag argument,
			// not once per flag, so `@set obj/attr=!safe wizard` isn't order-dependent on
			// whether "!safe" or "wizard" is processed first.
			var flagTokens = MModule.splitList(MModule.single(" "), args["1"].Message!)
				.Select(MModule.plainText)
				.ToList();

			var flagResult = await AttributeService!.SetAttributeFlagsAsync(executor, realLocated, maybeAttribute, flagTokens);

			if (flagResult.IsT1)
			{
				await NotifyService!.Notify(executor, flagResult.AsT1.Value, executor);
			}

			return new CallState(flagResult.Match(_ => string.Empty, failure => failure.Value));
		}

		var maybeColonLocation = MModule.indexOf(args["1"].Message!, ":");
		if (maybeColonLocation > -1)
		{
			var arg1 = args["1"].Message!;
			var attribute = MModule.substring(0, maybeColonLocation, arg1);
			var content = MModule.substring(maybeColonLocation + 1, MModule.getLength(arg1), arg1);

			var setResult =
				await AttributeService!.SetAttributeAsync(executor, realLocated, MModule.plainText(attribute), content);

			if (setResult.IsT0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeSet), executor,
					realLocated.Object().Name, MModule.plainText(attribute));
			}
			else
			{
				await NotifyService!.Notify(executor, setResult.AsT1.Value, executor);
			}

			return new CallState(setResult.Match(
				_ => $"{realLocated.Object().Name}/{args["0"].Message}",
				failure => failure.Value));
		}

		foreach (var flag in MModule.splitList(MModule.single(" "), args["1"].Message!))
		{
			await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, realLocated, flag.ToPlainText(), true);
		}

		return CallState.Empty;
	}


	[SharpCommand(Name = "@CHOWN", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2, ParameterNames = ["object", "player"])]
	public static async ValueTask<Option<CallState>> ChangeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var newOwnerName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, newOwnerName, LocateFlags.All,
					async newOwnerObj =>
					{
						if (!newOwnerObj.IsPlayer)
						{
							return await NotifyService!.NotifyAndReturn(
								executor.Object().DBRef,
								errorReturn: ErrorMessages.Returns.InvalidPlayer,
								notifyMessage: ErrorMessages.Notifications.MustBePlayer,
								shouldNotify: true);
						}

						var result = await ManipulateSharpObjectService!.SetOwner(executor, obj, newOwnerObj.AsPlayer, true);

						if (!preserve)
						{
							if (await obj.HasFlag("WIZARD"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
							}
							if (await obj.HasFlag("ROYALTY"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
							}
							await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "HALT", false);
						}

						return result;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@DESTROY", Switches = ["OVERRIDE"], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Destroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var override_ = parser.CurrentState.Switches.Contains("OVERRIDE");

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj => await DestroyObjectAsync(parser, executor, obj, override_)
		);
	}

	/// <summary>
	/// Core destroy logic shared by <c>@destroy</c> and <c>@nuke</c>.
	/// Mirrors PennMUSH <c>what_to_destroy()</c> + <c>pre_destroy()</c> + the player-specific parts
	/// of <c>clear_player()</c> that must happen at the "mark GOING" phase (channel chown,
	/// surviving-object chown, attribute ownership reassignment) because SharpMUSH does not yet
	/// have a live purge cycle.
	/// <para>
	/// For players, all of the above is handled by <see cref="HandlePlayerPossessionsAsync"/>.
	/// Lock expressions are left unchanged per PennMUSH invariants
	/// ("we allow indirect locks to refer to destroyed objects").
	/// </para>
	/// </summary>
	private static async ValueTask<CallState> DestroyObjectAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject obj,
		bool override_)
	{
		// --- Edge-case guards (PennMUSH src/destroy.c what_to_destroy) ---

		// Guests may not destroy anything.
		if (await executor.IsGuest())
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.GuestCantDestroy,
				shouldNotify: true);
		}

		// Nobody may destroy God.
		if (obj.IsGod())
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.DestroyGodBlasphemous,
				shouldNotify: true);
		}

		// Objects already marked GOING_TWICE are effectively garbage.
		if (await obj.HasFlag("GOING_TWICE"))
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.AlreadyDestroyed,
				shouldNotify: true);
		}

		// Protect special configuration objects (player_start, master_room, base_room, default_home).
		var dbConfig = Configuration!.CurrentValue.Database;
		var objKey = obj.Object().Key;
		if (objKey == dbConfig.PlayerStart || objKey == dbConfig.MasterRoom
			|| objKey == dbConfig.BaseRoom || objKey == dbConfig.DefaultHome)
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.TooSpecialToDestroy,
				shouldNotify: true);
		}

		// --- Standard permission and safety checks ---

		if (!await PermissionService!.Controls(executor, obj))
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		if (await obj.HasFlag("SAFE") && !override_)
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.SafeObject,
				notifyMessage: ErrorMessages.Notifications.SafeObjectUseNuke,
				shouldNotify: true);
		}

		// Player-specific guards (PennMUSH what_to_destroy, TYPE_PLAYER case)
		if (obj.IsPlayer)
		{
			// Only a wizard can destroy a player.
			if (!await executor.IsWizard())
			{
				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.NoSuicideAllowed,
					shouldNotify: true);
			}

			// Only God can destroy another wizard.
			if (await obj.IsWizard() && !executor.IsGod())
			{
				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.EvenYouCantDoThat,
					shouldNotify: true);
			}

			// Connected players may not be destroyed.
			var isConnected = await ConnectionService!
				.Get(obj.Object().DBRef)
				.AnyAsync(x => x.State == IConnectionService.ConnectionState.LoggedIn);
			if (isConnected)
			{
				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.MayNotDestroyConnectedPlayer,
					shouldNotify: true);
			}

			// Plain @destroy cannot target a player — @nuke (= override) is required.
			if (!override_)
			{
				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.MustUseNukeToDestroyPlayer,
					shouldNotify: true);
			}
		}

		if (await obj.HasFlag("GOING"))
		{
			// Phase 2b: object-lifecycle destroy seam. Fired while the object is still in the DB so a plugin
			// hook can still read it; this is the second-stage (GOING -> GOING_TWICE) commit.
			await NotifyObjectDestroyingAsync(parser, obj.Object().DBRef);

			await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "GOING_TWICE", false);
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Destroyed), executor);

			// NOTE: Actual object deletion from database requires a garbage collection system.
			// Objects marked GOING_TWICE will be cleaned up by a future purge process.
			return CallState.Empty;
		}

		// For players: handle possessions and channels before marking GOING.
		// This combines PennMUSH's pre_destroy (mark possessions GOING) and the
		// object/channel chown portion of clear_player (which runs at purge time in
		// PennMUSH but is done here because SharpMUSH lacks a live purge cycle).
		if (obj.IsPlayer)
		{
			await HandlePlayerPossessionsAsync(parser, executor, obj);
		}

		// Phase 2b: object-lifecycle destroy seam. The object is about to be marked GOING (scheduled for
		// destruction) but still present in the DB, so a plugin hook can read it before it is gone.
		await NotifyObjectDestroyingAsync(parser, obj.Object().DBRef);

		await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "GOING", false);

		var destroyMsg = obj.IsPlayer
			? string.Format(ErrorMessages.Notifications.ObjectAndPossessionsScheduledDestroyedFormat, obj.Object().Name)
			: string.Format(ErrorMessages.Notifications.ObjectScheduledDestroyedFormat, obj.Object().Name);
		await NotifyService!.Notify(executor, destroyMsg, executor);

		try
		{
			await AttributeService!.EvaluateAttributeFunctionAsync(
				parser, executor, obj, "ADESTROY", new Dictionary<string, CallState>(), evalParent: false);
		}
		catch (Exception)
		{
			// Ignore errors from @adestroy evaluation - attribute may not exist or may fail
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Handles the player-specific parts of destruction:
	/// <list type="bullet">
	///   <item>Channels owned by the player are chowned to the probate player.</item>
	///   <item>
	///     Objects owned by the player (other than the player themselves) are either
	///     marked GOING (to be destroyed at the next purge cycle) or chowned to the
	///     probate player, depending on <c>destroy_possessions</c> and <c>really_safe</c>
	///     config options — matching <c>clear_player()</c> in PennMUSH.
	///   </item>
	///   <item>
	///     All attributes whose creator is the deleted player are bulk-reassigned to the
	///     probate player via <see cref="ReassignAttributeOwnerCommand"/>.
	///     This is done after the chown and channel-chown steps so that any objects
	///     already marked GOING (scheduled for deletion) can be skipped, reducing the
	///     number of attributes that need to be reassigned when
	///     <c>destroy_possessions</c> is enabled.
	///     PennMUSH defers this to <c>dbck()</c>, but SharpMUSH does it eagerly at
	///     deletion time to avoid leaving dangling attribute-owner references in the database.
	///   </item>
	/// </list>
	/// <para>Lock expressions are left unchanged per PennMUSH invariants.</para>
	/// </summary>
	private static async ValueTask NotifyObjectDestroyingAsync(IMUSHCodeParser parser, DBRef obj)
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

	private static async ValueTask HandlePlayerPossessionsAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject playerObj)
	{
		var config = Configuration!.CurrentValue.Command;
		var playerDbRefNumber = playerObj.Object().DBRef.Number;

		// Resolve the probate player; fall back to God (#1) if the config value is invalid.
		var probateDbRef = new DBRef((int)config.ProbateJudge);
		var probateNode = await Mediator!.Send(new GetObjectNodeQuery(probateDbRef));
		SharpPlayer probatePlayer;
		if (!probateNode.IsNone && probateNode.Known.IsPlayer)
		{
			probatePlayer = probateNode.Known.AsPlayer;
		}
		else
		{
			Logger?.LogWarning(
				"probate_judge config option (#{ProbateDbRef}) is set to an invalid object; falling back to God (#1).",
				probateDbRef.Number);
			var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
			if (godNode.IsNone || !godNode.Known.IsPlayer)
			{
				Logger?.LogError(
					"God (#1) is not a valid player; cannot proceed with player possession chown during deletion.");
				return; // Cannot proceed without a valid probate player.
			}
			probatePlayer = godNode.Known.AsPlayer;
		}

		// --- Channels: always chown to probate (PennMUSH chan_chownall) ---
		var channels = Mediator.CreateStream(new GetChannelListQuery());
		await foreach (var channel in channels)
		{
			var channelOwner = await channel.Owner.WithCancellation(CancellationToken.None);
			if (channelOwner.Object.DBRef.Number != playerDbRefNumber)
				continue;

			await Mediator.Send(new UpdateChannelOwnerCommand(channel, probatePlayer));
		}

		// --- Possessions (PennMUSH clear_player object loop) ---
		var objects = Mediator.CreateStream(new GetAllTypedObjectsQuery());
		await foreach (var obj in objects)
		{
			var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);

			if (objOwner.Object.DBRef.Number != playerDbRefNumber)
				continue;

			if (obj.Object().DBRef.Number == playerDbRefNumber)
				continue; // Never process the player themselves.

			// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed
			var fullObj = obj;

			// Determine whether this object should be chowned to probate or destroyed.
			// Logic mirrors PennMUSH clear_player():
			//   chown  if: !destroy_possessions
			//          or: really_safe && SAFE flag is set
			//   destroy otherwise (when destroy_possessions is on)
			bool chownToProbate;
			if (!config.DestroyPossessions)
			{
				chownToProbate = true;
			}
			else if (config.ReallySafe && await fullObj.HasFlag("SAFE"))
			{
				chownToProbate = true;
			}
			else
			{
				chownToProbate = false;
			}

			if (chownToProbate)
			{
				await Mediator.Send(new SetObjectOwnerCommand(fullObj, probatePlayer));
			}
			else
			{
				// Pre-destroy: mark for destruction, matching PennMUSH pre_destroy().
				await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, fullObj, "GOING", false);
			}
		}

		// --- Attribute ownership: bulk-reassign all attributes owned by the deleted player ---
		// Done after the chown and channel-chown passes so that objects already marked GOING
		// (scheduled for deletion) can be excluded, reducing unnecessary work when
		// destroy_possessions is enabled.
		// PennMUSH defers this to dbck(), but we do it eagerly to keep the database consistent.
		await Mediator.Send(new ReassignAttributeOwnerCommand(playerObj.AsPlayer, probatePlayer));
	}

	[SharpCommand(Name = "@LINK", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2, ParameterNames = ["object", "destination"])]
	public static async ValueTask<Option<CallState>> Link(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var exitName = args["0"].Message!.ToPlainText();
		var destName = args["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, exitName, LocateFlags.All,
			async exitObj =>
			{
				if (!await PermissionService!.Controls(executor, exitObj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (exitObj.IsExit)
				{
					if (destName.Equals(LinkTypeHome, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService!.SetAttributeAsync(executor, exitObj, AttrLinkType, MModule.single(LinkTypeHome));
						await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedToHome), executor);
						return CallState.Empty;
					}
					else if (destName.Equals(LinkTypeVariable, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService!.SetAttributeAsync(executor, exitObj, AttrLinkType, MModule.single(LinkTypeVariable));
						await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedToVariable), executor);
						return CallState.Empty;
					}

					return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							// An exit may lead to any container — room, player or thing (PennMUSH can_link_to).
							// Only another exit is not a place you can end up.
							if (!destObj.IsContainer)
							{
								return await NotifyService!.NotifyAndReturn(
										executor.Object().DBRef,
										errorReturn: ErrorMessages.Returns.InvalidDestination,
										notifyMessage: ErrorMessages.Notifications.InvalidDestinationExit,
										shouldNotify: true);
							}

							var destination = destObj.AsContainer;

							if (!await CanLinkTo(executor, destObj))
							{
								return await NotifyService!.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.PermissionDenied,
									notifyMessage: ErrorMessages.Notifications.CantLinkToThat,
									shouldNotify: true);
							}

							var exitOwner = await exitObj.Object().Owner.WithCancellation(CancellationToken.None);
							var executorObj = executor.Object();
							var executorOwner = await executorObj.Owner.WithCancellation(CancellationToken.None);

							var exitNotControlled = !await PermissionService!.Controls(executor, exitObj);
							var isOwnedByOther = exitOwner.Object.Id != executorOwner.Object.Id;

							// When linking an exit owned by someone else that executor doesn't control:
							// Check @lock/link, transfer ownership, and set HALT flag
							if (isOwnedByOther && exitNotControlled)
							{
								var linkLockPasses = LockService!.Evaluate(LockType.Link, exitObj, executor);
								if (!linkLockPasses)
								{
									return await NotifyService!.NotifyAndReturn(
										executor.Object().DBRef,
										errorReturn: ErrorMessages.Returns.PermissionDenied,
										notifyMessage: ErrorMessages.Notifications.DontPassLinkLock,
										shouldNotify: true);
								}

								if (executor.IsPlayer)
								{
									try
									{
										await Mediator!.Send(new SetObjectOwnerCommand(exitObj, executor.AsPlayer));
									}
									catch (Exception)
									{
										return await NotifyService!.NotifyAndReturn(
											executor.Object().DBRef,
											errorReturn: ErrorMessages.Returns.PermissionDenied,
											notifyMessage: ErrorMessages.Notifications.FailedToTransferOwnership,
											shouldNotify: true);
									}
								}

								// Set HALT flag to prevent looping
								await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, exitObj, "HALT", true);
							}

							await AttributeService!.SetAttributeAsync(executor, exitObj, AttrLinkType, MModule.empty());

							await Mediator!.Send(new LinkExitCommand(exitObj.AsExit, destination));

							await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedExitToRoom), executor, exitObj.Object().DBRef.Number, destination.Object().DBRef.Number);
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
							if (!destObj.IsRoom)
							{
								return await NotifyService!.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.InvalidDestination,
									notifyMessage: ErrorMessages.Notifications.HomeMustBeRoom,
									shouldNotify: true);
							}

							// Convert to AnySharpContent for SetObjectHomeCommand
							var contentObj = exitObj.AsContent;
							await Mediator!.Send(new SetObjectHomeCommand(contentObj, destObj.AsRoom));
							await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HomeSet), executor);
							return CallState.Empty;
						}
					);
				}
				else if (exitObj.IsRoom)
				{
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (!destObj.IsRoom)
							{
								return await NotifyService!.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.InvalidDestination,
									notifyMessage: ErrorMessages.Notifications.DropToMustBeRoom,
									shouldNotify: true);
							}

							await Mediator!.Send(new LinkRoomCommand(exitObj.AsRoom, destObj.AsRoom));
							await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DropToSet), executor);
							return CallState.Empty;
						}
					);
				}

				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.InvalidObjectType,
					notifyMessage: ErrorMessages.Notifications.InvalidObjectTypeForLinking,
					shouldNotify: true);
			}
		);
	}

	[SharpCommand(Name = "@NUKE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Nuke(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// @nuke is @destroy/override: it bypasses the SAFE flag and the "use @nuke" player guard.
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj => await DestroyObjectAsync(parser, executor, obj, override_: true)
		);
	}

	[SharpCommand(Name = "@UNDESTROY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> UnDestroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (!await obj.HasFlag("GOING"))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.NotGoing,
						notifyMessage: ErrorMessages.Notifications.NotMarkedForDestruction,
						shouldNotify: true);
				}

				if (await obj.HasFlag("GOING"))
				{
					await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "!GOING", false);
				}
				if (await obj.HasFlag("GOING_TWICE"))
				{
					await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "!GOING_TWICE", false);
				}

				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SparedFromDestructionFormat), executor, obj.Object().Name);

				try
				{
					await AttributeService!.EvaluateAttributeFunctionAsync(
						parser, executor, obj, "STARTUP", new Dictionary<string, CallState>(), evalParent: false);
				}
				catch (Exception)
				{
					// Ignore errors from @startup evaluation - attribute may not exist or may fail
				}

				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@CHZONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "zone"])]
	public static async ValueTask<Option<CallState>> ChangeZone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var zoneName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					await Mediator!.Send(new UnsetObjectZoneCommand(obj));
					await NotifyService!.Notify(executor, "Zone cleared.", executor);
					return CallState.Empty;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj =>
					{
						bool canZone = await PermissionService!.Controls(executor, zoneObj);

						if (!canZone && !LockService!.Evaluate(LockType.ChZone, zoneObj, executor))
						{
							return await NotifyService!.NotifyAndReturn(
									executor.Object().DBRef,
									errorReturn: ErrorMessages.Returns.PermissionDenied,
									notifyMessage: ErrorMessages.Notifications.PermissionDeniedCannotZoneTo,
									shouldNotify: true);
						}

						// Check for cycles before setting the zone
						if (!await HelperFunctions.SafeToAddZone(Mediator!, Database!, obj, zoneObj))
						{
							return await NotifyService!.NotifyAndReturn(
								executor.Object().DBRef,
								errorReturn: ErrorMessages.Returns.ZoneLoop,
								notifyMessage: ErrorMessages.Notifications.CantMakeCircularZones,
								shouldNotify: true);
						}

						await Mediator!.Send(new SetObjectZoneCommand(obj, zoneObj));

						// Default ChZone lock is the zone object itself (allows controlled objects)
						if (!zoneObj.Object().Locks.ContainsKey("ChZone"))
						{
							await Mediator.Send(new SetLockCommand(zoneObj.Object(), "ChZone", zoneObj.Object().DBRef.ToString()));
						}

						// Clear privileged flags and powers unless /preserve is used
						if (!preserve && !obj.IsPlayer)
						{
							if (await obj.HasFlag("WIZARD"))
							{
								await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
							}
							if (await obj.HasFlag("ROYALTY"))
							{
								await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
							}
							if (await obj.HasFlag("TRUST"))
							{
								await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "!TRUST", false);
							}

							var allPowers = obj.Object().Powers.Value;
							await foreach (var power in allPowers)
							{
								await Mediator!.Send(new UnsetObjectPowerCommand(obj, power));
							}
						}

						await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZoneChanged), executor);
						return CallState.Empty;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@DIG", Switches = ["TELEPORT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 6, ParameterNames = ["name", "exits"])]
	public static async ValueTask<Option<CallState>> Dig(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// NOTE: We discard arguments 4-6.
		var executorBase = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executor = executorBase.Object();
		var roomName = parser.CurrentState.Arguments["0"].Message!;
		parser.CurrentState.Arguments.TryGetValue("1", out var exitToCallState);
		parser.CurrentState.Arguments.TryGetValue("2", out var exitFromCallState);
		var exitTo = exitToCallState?.Message;
		var exitFrom = exitFromCallState?.Message;

		if (string.IsNullOrWhiteSpace(parser.CurrentState.Arguments["0"].Message!.ToString()))
		{
			await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.DigWhat), executorBase);
			return new CallState(ErrorMessages.Returns.NoRoomNameSpecified);
		}

		// NOTE: Additional permission checks needed:
		// - Can executor create rooms (quota check)
		// - Does executor have DIG permission

		var response = await Mediator!.Send(new CreateRoomCommand(MModule.plainText(roomName),
			await executor.Owner.WithCancellation(CancellationToken.None)));
		await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.RoomCreatedWithNumberFormat), executorBase, roomName, response.Number);

		var creatorZone = await executor.Zone.WithCancellation(CancellationToken.None);
		if (!creatorZone.IsNone)
		{
			var newRoom = await Mediator.Send(new GetObjectNodeQuery(response));
			if (!newRoom.IsNone)
			{
				// Check for cycles before inheriting zone from creator
				if (await HelperFunctions.SafeToAddZone(Mediator, Database!, newRoom.Known, creatorZone.Known))
				{
					await Mediator.Send(new SetObjectZoneCommand(newRoom.Known, creatorZone.Known));
				}
			}
		}

		if (!string.IsNullOrWhiteSpace(exitTo?.ToString()))
		{
			var exitToName = MModule.plainText(exitTo).Split(";");
			// CAN CREATE EXIT HERE?
			// CAN LINK TO DESTINATION?

			var toExitResponse = await Mediator.Send(new CreateExitCommand(exitToName.First(),
				exitToName.Skip(1).ToArray(), await executorBase.Where(),
				await executor.Owner.WithCancellation(CancellationToken.None)));
			await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.OpenedExit), executorBase, $"#{toExitResponse.Number}");
			await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.TryingToLink), executorBase);

			var newRoomObject = await Mediator.Send(new GetObjectNodeQuery(response));
			var newExitObject = await Mediator.Send(new GetObjectNodeQuery(toExitResponse));

			await Mediator.Send(new LinkExitCommand(newExitObject.AsExit, newRoomObject.AsRoom));

			await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.LinkedExitToRoom), executorBase, toExitResponse.Number, response.Number);
		}

		if (!string.IsNullOrWhiteSpace(exitFrom?.ToString()))
		{
			// CAN CREATE EXIT THERE?
			// CAN LINK BACK TO CURRENT ROOM?

			var exitFromName = MModule.plainText(exitFrom).Split(";");
			var newRoomObject = await Mediator.Send(new GetObjectNodeQuery(response));

			var fromExitResponse = await Mediator.Send(new CreateExitCommand(exitFromName.First(),
				exitFromName.Skip(1).ToArray(), newRoomObject.AsRoom,
				await executor.Owner.WithCancellation(CancellationToken.None)));
			var newExitObject = await Mediator.Send(new GetObjectNodeQuery(fromExitResponse));

			await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.OpenedExit), executorBase, $"#{fromExitResponse.Number}");
			await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.TryingToLink), executorBase);

			var where = await executorBase.Where();
			await Mediator.Send(new LinkExitCommand(newExitObject.AsExit, where));

			await NotifyService!.NotifyLocalized(executor.DBRef, nameof(ErrorMessages.Notifications.LinkedExitToRoom), executorBase, fromExitResponse.Number, where.Object().DBRef.Number);
		}

		return new CallState(response.ToString());
	}

	[SharpCommand(Name = "@LOCK", Switches = ["*"], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "locktype", "key"])]
	public static async ValueTask<Option<CallState>> Lock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var lockKey = args["1"].Message!.ToPlainText();

		var lockType = "Basic";
		if (parser.CurrentState.Switches.Any())
		{
			var switchName = parser.CurrentState.Switches.First();
			// Resolve to canonical lock name (e.g. "USE" -> "Use") if it's a known system lock
			var canonicalName = LockService!.SystemLocks.Keys
				.FirstOrDefault(k => string.Equals(k, switchName, StringComparison.OrdinalIgnoreCase));
			lockType = canonicalName ?? switchName;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				await Mediator!.Send(new SetLockCommand(obj.Object(), lockType, lockKey, executor));
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectLocked), executor, obj.Object().Name, obj.Object().DBRef.Number, lockType);
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@UNLOCK", Switches = ["*"], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object", "locktype"])]
	public static async ValueTask<Option<CallState>> Unlock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();

		var lockType = "Basic";
		if (parser.CurrentState.Switches.Any())
		{
			var switchName = parser.CurrentState.Switches.First();
			var canonicalName = LockService!.SystemLocks.Keys
				.FirstOrDefault(k => string.Equals(k, switchName, StringComparison.OrdinalIgnoreCase));
			lockType = canonicalName ?? switchName;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				await Mediator!.Send(new UnsetLockCommand(obj.Object(), lockType));
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectUnlocked), executor, obj.Object().Name, obj.Object().DBRef.Number, lockType);
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@ELOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public static async ValueTask<Option<CallState>> ELock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// @ELOCK is an alias for @lock/enter
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var lockKey = args["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				await Mediator!.Send(new SetLockCommand(obj.Object(), "Enter", lockKey, executor));
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectLocked), executor, obj.Object().Name, obj.Object().DBRef.Number, "Enter");
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@EUNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> EUnlock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// @EUNLOCK is an alias for @unlock/enter
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				await Mediator!.Send(new UnsetLockCommand(obj.Object(), "Enter"));
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectUnlocked), executor, obj.Object().Name, obj.Object().DBRef.Number, "Enter");
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@ULOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public static async ValueTask<Option<CallState>> ULock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// @ULOCK is an alias for @lock/use
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var lockKey = args["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				await Mediator!.Send(new SetLockCommand(obj.Object(), "Use", lockKey, executor));
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectLocked), executor, obj.Object().Name, obj.Object().DBRef.Number, "Use");
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@UUNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> UUnlock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		// @UUNLOCK is an alias for @unlock/use
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				await Mediator!.Send(new UnsetLockCommand(obj.Object(), "Use"));
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectUnlocked), executor, obj.Object().Name, obj.Object().DBRef.Number, "Use");
				return CallState.Empty;
			}
		);
	}

	/// <summary>
	/// PennMUSH <c>can_link_to</c> (<c>mushdb.h:87</c>): you may point an exit at somewhere you control,
	/// or at somewhere flagged LINK_OK. Both <c>@link</c> and <c>@open</c> gate on this — without it,
	/// accepting any container as a destination would let anyone link an exit into someone else's object.
	/// </summary>
	private static async ValueTask<bool> CanLinkTo(AnySharpObject executor, AnySharpObject destination)
	{
		if (await PermissionService!.Controls(executor, destination))
		{
			return true;
		}

		var destinationFlags = await destination.Object().Flags.Value.ToArrayAsync();

		return destinationFlags.Any(f => f.Name.Equals("LINK_OK", StringComparison.OrdinalIgnoreCase));
	}

	[SharpCommand(Name = "@OPEN", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 5, ParameterNames = ["exit", "destination"])]
	public static async ValueTask<Option<CallState>> Open(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var exitName = args["0"].Message!.ToPlainText();

		var exitParts = exitName.Split(";");
		var primaryName = exitParts[0];
		var aliases = exitParts.Skip(1).ToArray();

		var sourceRoom = await executor.Where();
		if (args.ContainsKey("2") && !string.IsNullOrWhiteSpace(args["2"].Message!.ToPlainText()))
		{
			var sourceRoomName = args["2"].Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, sourceRoomName, LocateFlags.All);

			if (locateResult.IsError || !locateResult.AsSharpObject.IsRoom)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SourceMustBeARoom), executor);
				return new CallState(ErrorMessages.Returns.NotARoom);
			}
			sourceRoom = locateResult.AsSharpObject.AsRoom;
		}

		if (!await PermissionService!.Controls(executor, sourceRoom.WithExitOption()))
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		var exitDbRef = await Mediator!.Send(new CreateExitCommand(
			primaryName,
			aliases,
			sourceRoom,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)
		));

		var creatorZone = await executor.Object().Zone.WithCancellation(CancellationToken.None);
		if (!creatorZone.IsNone)
		{
			var newExit = await Mediator.Send(new GetObjectNodeQuery(exitDbRef));
			if (!newExit.IsNone)
			{
				// Check for cycles before inheriting zone from creator
				if (await HelperFunctions.SafeToAddZone(Mediator, Database!, newExit.Known, creatorZone.Known))
				{
					await Mediator.Send(new SetObjectZoneCommand(newExit.Known, creatorZone.Known));
				}
			}
		}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OpenedExit), executor, $"#{exitDbRef.Number}");

		if (args.ContainsKey("1") && !string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
		{
			var destName = args["1"].Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, destName, LocateFlags.All);

			if (locateResult.IsError)
			{
				// LocateAndNotifyIfInvalidWithCallState has already said why.
				return new CallState(exitDbRef.ToString());
			}

			// An exit may lead to any container — room, player or thing (PennMUSH can_link_to). Anything
			// else, or anywhere the executor may not link into, is reported rather than leaving the exit
			// silently unlinked.
			if (!locateResult.AsSharpObject.IsContainer
					|| !await CanLinkTo(executor, locateResult.AsSharpObject))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantLinkToThat), executor);
				return new CallState(exitDbRef.ToString());
			}

			var exitObj = await Mediator.Send(new GetObjectNodeQuery(exitDbRef));
			await Mediator.Send(new LinkExitCommand(exitObj.AsExit, locateResult.AsSharpObject.AsContainer));
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedToNameFormat), executor, destName);
		}

		return new CallState(exitDbRef.ToString());
	}

	[SharpCommand(Name = "@CLONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "name", "cost"])]
	public static async ValueTask<Option<CallState>> Clone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var location = await Mediator!.Send(new GetObjectNodeQuery(defaultHomeDbref));

		if (location.IsNone || location.IsExit)
		{
			return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.NotARoom,
					notifyMessage: ErrorMessages.Notifications.DefaultHomeLocationInvalid,
					shouldNotify: true);
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (obj.IsPlayer)
				{
					return await NotifyService!.NotifyAndReturn(
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
					cloneDbRef = await Mediator!.Send(new CreateThingCommand(
						newName,
						await executor.Where(),
						owner,
						location.Known.AsContainer
					));
				}
				else if (obj.IsRoom)
				{
					cloneDbRef = await Mediator!.Send(new CreateRoomCommand(
						newName,
						owner
					));
				}
				else if (obj.IsExit)
				{
					var nameParts = newName.Split(";");
					cloneDbRef = await Mediator!.Send(new CreateExitCommand(
						nameParts[0],
						nameParts.Skip(1).ToArray(),
						await executor.Where(),
						owner
					));
				}
				else
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.InvalidObjectType,
						notifyMessage: ErrorMessages.Notifications.CannotCloneThisObjectType,
						shouldNotify: true);
				}

				var clonedObjOptional = await Mediator!.Send(new GetObjectNodeQuery(cloneDbRef));
				var clonedObj = clonedObjOptional.WithoutNone();

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

				await foreach (var sourceAttribute in Mediator!.CreateStream(
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
					// by SetAttributeAsync (ArangoDatabase.Attributes.cs:608-675) - the same
					// missing-root hazard the no_clone propagation above exists to avoid.
					if (attr.IsNoCopy() || attr.Name.StartsWith("_") || parentSkipped)
					{
						skippedAttributes.Add(longName);
						continue;
					}

					// AL_CREATOR(ptr) is passed through unchanged in atr_cpy (attrib.c:1706) - a
					// cloned attribute keeps its original creator, not the cloner.
					var creator = await attr.Owner.WithCancellation(CancellationToken.None) ?? owner;
					var setResult = await AttributeService!.SetAttributeAsync(executor, clonedObj, longName, attr.Value, creator);

					// A failed set means the branch was NOT actually copied. Treating it as
					// skipped keeps the invariant this whole loop depends on: a LongName only
					// avoids the skip set if it genuinely landed on the clone. Without this, a
					// child under a branch that failed to set would still see its parent as
					// "not skipped" and auto-vivify a stripped-down stand-in via
					// SetAttributeAsync's own auto-vivification (ArangoDatabase.Attributes.cs:
					// 608-675) - the exact hazard this propagation exists to prevent. Unreachable
					// today (the clone's owner always controls the freshly-created destination),
					// but one permission change away from live.
					if (setResult.IsT1)
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
					var destAttribute = await Mediator!.CreateStream(new GetAttributeQuery(clonedObj.Object().DBRef, attrPath))
						.LastOrDefaultAsync();

					if (destAttribute is not null)
					{
						var sourceFlagNames = attr.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
						var destFlagNames = destAttribute.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

						foreach (var flag in destAttribute.Flags.Where(f => !sourceFlagNames.Contains(f.Name)))
						{
							await Mediator!.Send(new UnsetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
						}

						foreach (var flag in attr.Flags.Where(f => !destFlagNames.Contains(f.Name)))
						{
							await Mediator!.Send(new SetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
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

				await foreach (var flag in obj.Object().Flags.Value)
				{
					if (preserve || (!flag.Name.Contains("WIZARD") && !flag.Name.Contains("ROYALTY")))
					{
						await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, clonedObj, flag.Name, false);
					}
				}

				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ClonedNewObjectFormat), executor, cloneDbRef.Number);
				return new CallState(cloneDbRef.ToString());
			}
		);
	}

	[SharpCommand(Name = "@MONIKER", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "moniker"])]
	public static async ValueTask<Option<CallState>> Moniker(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (!args.ContainsKey("1") || string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
				{
					await AttributeService!.SetAttributeAsync(executor, obj, "MONIKER", MModule.single(""));
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MonikerCleared), executor);
					return CallState.Empty;
				}

				var moniker = args["1"].Message!;
				await AttributeService!.SetAttributeAsync(executor, obj, "MONIKER", moniker);
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MonikerSet), executor);
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@PARENT", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "parent"])]
	public static async ValueTask<Option<CallState>> Parent(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!await PermissionService!.Controls(executor, target))
				{
					return await NotifyService!.NotifyAndReturn(
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

						return await ManipulateSharpObjectService!.UnsetParent(executor, target, true);
					default:

						return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
							parser, executor, executor,
							args["1"].Message!.ToPlainText(), LocateFlags.All,
							async newParent
								=> await ManipulateSharpObjectService!.SetParent(executor, target, newParent, true));
				}
			}
		);
	}


	[SharpCommand(Name = "@UNLINK", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Unlink(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (obj.IsExit)
				{
					await AttributeService!.SetAttributeAsync(executor, obj, AttrLinkType, MModule.empty());

					await Mediator!.Send(new UnlinkExitCommand(obj.AsExit));
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.UnlinkedExit), executor, obj.Object().DBRef.Number);
					return CallState.Empty;
				}
				else if (obj.IsRoom)
				{
					await Mediator!.Send(new UnlinkRoomCommand(obj.AsRoom));
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DropToRemoved), executor);
					return CallState.Empty;
				}

				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.InvalidObjectType,
					notifyMessage: ErrorMessages.Notifications.InvalidObjectTypeGeneric,
					shouldNotify: true);
			}
		);
	}
}