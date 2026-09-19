using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@CHOWN", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2, ParameterNames = ["object", "player"])]
	public async ValueTask<Option<CallState>> ChangeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var newOwnerName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

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

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, newOwnerName, LocateFlags.All,
					async newOwnerObj =>
					{
						if (newOwnerObj is not SharpPlayer newOwnerPlayer)
						{
							return await NotifyService.NotifyAndReturn(
								executor.Object().DBRef,
								errorReturn: ErrorMessages.Returns.InvalidPlayer,
								notifyMessage: ErrorMessages.Notifications.MustBePlayer,
								shouldNotify: true);
						}

						var result = await ManipulateSharpObjectService.SetOwner(executor, obj, newOwnerPlayer, true);

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

	[SharpCommand(Name = "@CHZONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "zone"])]
	public async ValueTask<Option<CallState>> ChangeZone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var zoneName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

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

				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					await Mediator.Send(new UnsetObjectZoneCommand(obj));
					await NotifyService.Notify(executor, "Zone cleared.", executor);
					return CallState.Empty;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj =>
					{
						// PennMUSH do_chzone (src/set.c:408-420) gates this on
						// `has_lock && eval_lock_with(...)`, with has_lock computed as
						// `getlock(zone, Chzone_Lock) != TRUE_BOOLEXP` and the comment "Note that an
						// object with no chzone-lock isn't valid". An unset lock evaluates #TRUE, so
						// asking only for the verdict hands every never-zone-locked object to every
						// mortal as a zone. The lock has to be *present* before its verdict counts.
						if (!await PermissionService.Controls(executor, zoneObj))
						{
							var zoneLock = await LockService.LookupAsync(zoneObj, nameof(LockType.ChZone), ExecutionBudget.CurrentToken);
							if (zoneLock is not ResolvedLock resolved ||
								!await LockService.Evaluate(resolved.Data.LockString, zoneObj, executor))
							{
								// Penn runs fail_lock() when the lock exists and refused, and a bare
								// notify when there was no lock to fail.
								if (zoneLock is ResolvedLock)
								{
									await DidItService.FailLockLocalized(parser, executor, zoneObj, LockType.ChZone,
										new LocalizedNotification(nameof(ErrorMessages.Notifications.PermissionDeniedCannotZoneTo)));
									return new CallState(ErrorMessages.Returns.PermissionDenied);
								}

								return await NotifyService.NotifyAndReturn(
										executor.Object().DBRef,
										errorReturn: ErrorMessages.Returns.PermissionDenied,
										notifyMessage: ErrorMessages.Notifications.PermissionDeniedCannotZoneTo,
										shouldNotify: true);
							}
						}

						// Check for cycles before setting the zone
						if (!await HelperFunctions.SafeToAddZone(Mediator, Database, obj, zoneObj))
						{
							return await NotifyService.NotifyAndReturn(
								executor.Object().DBRef,
								errorReturn: ErrorMessages.Returns.ZoneLoop,
								notifyMessage: ErrorMessages.Notifications.CantMakeCircularZones,
								shouldNotify: true);
						}

						// Clear privileged flags and powers unless /preserve is used.
						//
						// Ahead of the zone change, and not after it as PennMUSH's do_chzone (src/set.c:373)
						// writes it: PennMUSH strips with clear_flag_internal() and destroy_flag_bitmask(),
						// which ask nobody's permission, so its one controls() check above is the whole
						// authorization. These go through ManipulateSharpObjectService, which checks Controls
						// itself — and Controls reads the object's *current* zone (PermissionService.Controls,
						// Zone Master Object branch). Once the zone has moved, an executor who held the object
						// only through the zone it is leaving no longer controls it, the strip is refused, and
						// @CHZONE reports "Zone changed." over an object that kept every power. Running the
						// strip first is what keeps the authorization checked at the top of this command the
						// one that governs it. Nothing below can fail, so the observable order is PennMUSH's.
						if (!preserve && !obj.IsPlayer)
						{
							if (await obj.HasFlag("WIZARD"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
							}
							if (await obj.HasFlag("ROYALTY"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
							}
							if (await obj.HasFlag("TRUST"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!TRUST", false);
							}

							// Same clearing @CHZONEALL uses: it materializes the collection before
							// unsetting and publishes ObjectFlagChangedNotification per power, which
							// the hand-rolled loop here did not.
							await ManipulateSharpObjectService.ClearAllPowers(executor, obj, false);
						}

						await Mediator.Send(new SetObjectZoneCommand(obj, zoneObj));

						// PennMUSH check_zone_lock (src/lock.c:962): a zone that has never been
						// zone-locked gets `=me` installed on it, written as GOD. It is a system
						// write on purpose — the executor who most needs it is the one who reached
						// this zone through its lock rather than through control, and so cannot
						// write to it.
						if (!zoneObj.Object().Locks.ContainsKey(nameof(LockType.ChZone)))
						{
							await LockService.SetSystemAsync(zoneObj, nameof(LockType.ChZone),
								$"=#{zoneObj.Object().DBRef.Number}", ExecutionBudget.CurrentToken);
						}

						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZoneChanged), executor);
						return CallState.Empty;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@CHOWNALL", Switches = ["PRESERVE", "THINGS", "ROOMS", "EXITS"],
		Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 2, ParameterNames = ["old-owner", "new-owner"])]
	public async ValueTask<Option<CallState>> Chown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		var preserve = switches.Contains("PRESERVE");

		if (args.Count < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChownAllUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var playerArg = args["0"].Message!.ToPlainText();
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg) switch
		{
			AnySharpObject and SharpPlayer oldOwner => await ChownAllFromAsync(parser, executor, oldOwner, switches, args,
				preserve),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> ChownAllFromAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpPlayer oldOwner, IEnumerable<string> switches, Dictionary<string, CallState> args, bool preserve)
	{
		var newOwner = executor;
		if (args.Count > 1)
		{
			var newOwnerArg = args["1"].Message!.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, newOwnerArg))
			{
				case AnySharpObject found:
					newOwner = found;
					break;
				case Error<CallState> error:
					return error.Value;
			}
		}

		var chownThings = switches.Contains("THINGS") || (!switches.Contains("ROOMS") && !switches.Contains("EXITS"));
		var chownRooms = switches.Contains("ROOMS") || (!switches.Contains("THINGS") && !switches.Contains("EXITS"));
		var chownExits = switches.Contains("EXITS") || (!switches.Contains("THINGS") && !switches.Contains("ROOMS"));

		var objects = Mediator.CreateStream(new GetAllTypedObjectsQuery());
		var count = 0;

		await foreach (var obj in objects)
		{
			var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);

			if (objOwner.Object.DBRef.Number != oldOwner.Object.DBRef.Number)
			{
				continue;
			}

			// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed

			var shouldChown = (chownThings && obj.IsThing) ||
												(chownRooms && obj.IsRoom) ||
												(chownExits && obj.IsExit);

			if (!shouldChown)
			{
				continue;
			}

			if (newOwner is not SharpPlayer newOwnerPlayer)
			{
				throw new InvalidOperationException("Objects are owned by players.");
			}

			await Mediator.Send(new SetObjectOwnerCommand(obj, newOwnerPlayer));
			count++;

			if (!preserve && !obj.IsPlayer)
			{
				if (await obj.HasFlag("WIZARD"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
				}
				if (await obj.HasFlag("ROYALTY"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
				}
				if (await obj.HasFlag("TRUST"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!TRUST", false);
				}
				await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "HALT", false);

				await ManipulateSharpObjectService.ClearAllPowers(executor, obj, false);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChownAllCompleteFormat), executor, count, oldOwner.Object.Name, newOwner.Object().Name);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@CHZONEALL", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD",
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["old-zone", "new-zone"])]
	public async ValueTask<Option<CallState>> ChangeZoneAll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var playerName = args["0"].Message!.ToPlainText();
		var zoneName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		// A player by name, so MAT_PMATCH: MAT_EVERYTHING carries MAT_PLAYER, which only answers to a
		// leading '*'.
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, playerName,
			LocateFlags.All | LocateFlags.MatchOptionalWildCardForPlayerName,
			async player =>
			{
				if (!player.IsPlayer)
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.InvalidPlayer,
						notifyMessage: ErrorMessages.Notifications.MustBePlayer,
						shouldNotify: true);
				}

				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					var allObjects = Mediator.CreateStream(new GetAllTypedObjectsQuery())!;
					var count = 0;

					await foreach (var obj in allObjects)
					{
						var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
						if (objOwner.Object.DBRef.Number == player.Object().DBRef.Number)
						{
							// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed
							await Mediator.Send(new UnsetObjectZoneCommand(obj));
							count++;
						}
					}

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZonesClearedForOwnerFormat), executor, count, player.Object().Name);
					return CallState.Empty;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj =>
					{
						var allObjects = Mediator.CreateStream(new GetAllTypedObjectsQuery())!;
						var count = 0;

						await foreach (var obj in allObjects)
						{
							var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
							if (objOwner.Object.DBRef.Number == player.Object().DBRef.Number)
							{
								// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed

								if (!await HelperFunctions.SafeToAddZone(Mediator, Database, obj, zoneObj))
								{
									continue;
								}

								await Mediator.Send(new SetObjectZoneCommand(obj, zoneObj));

								if (!preserve && !obj.IsPlayer)
								{
									if (await obj.HasFlag("WIZARD"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
									}
									if (await obj.HasFlag("ROYALTY"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
									}
									if (await obj.HasFlag("TRUST"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!TRUST", false);
									}

									await ManipulateSharpObjectService.ClearAllPowers(executor, obj, false);
								}

								count++;
							}
						}

						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZoneSetForOwnerFormat), executor, zoneObj.Object().Name, count, player.Object().Name);
						return CallState.Empty;
					}
				);
			}
		);
	}
}
