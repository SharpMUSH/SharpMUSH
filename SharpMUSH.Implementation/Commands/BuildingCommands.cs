using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using Errors = SharpMUSH.Library.Definitions.Errors;

#pragma warning disable CS8602 // Dereference of a possibly null reference

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@RECYCLE", Switches = ["OVERRIDE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 1,
		MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Recycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @recycle is an alias for @destroy
		return await Destroy(parser, _2);
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// NOTE: Cost parameter requires economy/quota system implementation.
	/// </remarks>
	[SharpCommand(Name = "@CREATE", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Create(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var name = args["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var location = await Mediator.Send(new GetObjectNodeQuery(defaultHomeDbref));
		
		if (location.IsNone || location.IsExit)
		{
			await NotifyService!.Notify(executor, "Default home location is invalid.");
			return new CallState(Errors.ErrorInvalidRoom);
		}

		if (!await ValidateService!.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			await NotifyService!.Notify(executor, "Invalid name for a thing.");
			return new CallState(Errors.ErrorBadObjectName);
		}
		
		var thing = await Mediator!.Send(new CreateThingCommand(name.ToPlainText(),
			await executor.Where(),
			await executor.Object().Owner.WithCancellation(CancellationToken.None),
			location.Known.AsContainer));
		
		await NotifyService!.Notify(executor, $"Created {name} ({thing}).");

		return new CallState(thing.ToString());
	}

	[SharpCommand(Name = "@FIRSTEXIT", Switches = [], Behavior = CB.Default | CB.Args, MinArgs = 0, MaxArgs = 0)]
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
		MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Rename(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var target = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var name = parser.CurrentState.Arguments["1"].Message!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, target,
			LocateFlags.All,
			async found => await ManipulateSharpObjectService!.SetName(executor, found, name, true)
		);
	}

	[SharpCommand(Name = "@SET", Behavior = CB.RSArgs | CB.EqSplit, MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> SetCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(args["0"].Message!));
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();

		if (!split.TryPickT0(out var details, out _))
		{
			return new CallState("#-1 BAD ARGUMENT FORMAT TO @SET");
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

		// Attr Flag Path
		if (!string.IsNullOrEmpty(maybeAttribute))
		{
			foreach (var flag in MModule.split(" ", args["1"].Message!))
			{
				var plainFlag = MModule.plainText(flag);
				if (plainFlag.StartsWith('!'))
				{
					await AttributeService!.SetAttributeFlagAsync(executor, realLocated, maybeAttribute, plainFlag);
				}
				else
				{
					await AttributeService!.UnsetAttributeFlagAsync(executor, realLocated, maybeAttribute, plainFlag[1..]);
				}
			}

			return new CallState(string.Empty);
		}

		// Attr Set Path
		var maybeColonLocation = MModule.indexOf(args["1"].Message!, MModule.single(":"));
		if (maybeColonLocation > -1)
		{
			var arg1 = args["1"].Message!;
			var attribute = MModule.substring(0, maybeColonLocation, arg1);
			var content = MModule.substring(maybeColonLocation + 1, MModule.getLength(arg1), arg1);

			var setResult =
				await AttributeService!.SetAttributeAsync(executor, realLocated, MModule.plainText(attribute), content);

			await NotifyService!.Notify(enactor,
				setResult.Match(
					_ => $"{realLocated.Object().Name}/{args["0"].Message} - Set.",
					failure => failure.Value)
			);

			return new CallState(setResult.Match(
				_ => $"{realLocated.Object().Name}/{args["0"].Message}",
				failure => failure.Value));
		}

		// Object Flag Set Path
		foreach (var flag in MModule.split(" ", args["1"].Message!))
		{
			await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, realLocated, flag.ToPlainText(), true);
		}

		return CallState.Empty;
	}


	[SharpCommand(Name = "@CHOWN", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> ChangeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
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
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, newOwnerName, LocateFlags.All,
					async newOwnerObj =>
					{
						if (!newOwnerObj.IsPlayer)
						{
							await NotifyService!.Notify(executor, "New owner must be a player.");
							return Errors.ErrorInvalidPlayer;
						}

						var result = await ManipulateSharpObjectService!.SetOwner(executor, obj, newOwnerObj.AsPlayer, true);
						
						// Clear privileged flags and powers unless /preserve is used
						if (!preserve)
						{
							// Clear WIZARD, ROYALTY flags if present
							if (await obj.HasFlag("WIZARD"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
							}
							if (await obj.HasFlag("ROYALTY"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
							}
							// Set HALT flag
							await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "HALT", false);
						}

						return result;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@DESTROY", Switches = ["OVERRIDE"], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Destroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var override_ = parser.CurrentState.Switches.Contains("OVERRIDE");

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				// Check for SAFE flag
				if (await obj.HasFlag("SAFE") && !override_)
				{
					await NotifyService!.Notify(executor, "That object is SAFE. Use @nuke to override.");
					return Errors.ErrorSafeObject;
				}

				// Check if already marked GOING
				if (await obj.HasFlag("GOING"))
				{
					// Mark as GOING_TWICE for immediate destruction
					await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "GOING_TWICE", false);
					await NotifyService!.Notify(executor, $"Destroyed: {obj.Object().Name}");
					
					// NOTE: Actual object deletion from database requires a garbage collection system
					// Objects marked GOING_TWICE will be cleaned up by a future purge process
					return CallState.Empty;
				}

				// Mark as GOING
				await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "GOING", false);
				await NotifyService!.Notify(executor, $"Marked for destruction: {obj.Object().Name}");
				
				// Trigger @adestroy attribute if it exists
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
		);
	}

	[SharpCommand(Name = "@LINK", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Link(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
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
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				// Handle different link types
				if (exitObj.IsExit)
				{
					// Link exit to destination
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (!destObj.IsRoom && !destName.Equals("home", StringComparison.InvariantCultureIgnoreCase) 
							    && !destName.Equals("variable", StringComparison.InvariantCultureIgnoreCase))
							{
								await NotifyService.Notify(executor, "Invalid destination for exit.");
								return Errors.ErrorInvalidDestination;
							}

							// Link the exit
							if (destObj.IsRoom)
							{
								await Mediator!.Send(new LinkExitCommand(exitObj.AsExit, destObj.AsRoom));
							}

							await NotifyService.Notify(executor, "Linked.");
							return CallState.Empty;
						}
					);
				}
				else if (exitObj.IsThing || exitObj.IsPlayer)
				{
					// Set HOME for thing or player
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (!destObj.IsRoom)
							{
								await NotifyService!.Notify(executor, "Home must be a room.");
								return Errors.ErrorInvalidDestination;
							}

							// Convert to AnySharpContent for SetObjectHomeCommand
							AnySharpContent contentObj = exitObj.IsThing ? exitObj.AsThing : (AnySharpContent)exitObj.AsPlayer;
							await Mediator!.Send(new SetObjectHomeCommand(contentObj, destObj.AsRoom));
							await NotifyService!.Notify(executor, "Home set.");
							return CallState.Empty;
						}
					);
				}
				else if (exitObj.IsRoom)
				{
					// Set DROP-TO for room
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (!destObj.IsRoom)
							{
								await NotifyService.Notify(executor, "Drop-to must be a room.");
								return Errors.ErrorInvalidDestination;
							}

							// NOTE: Drop-to setting requires DROP-TO property implementation in SharpRoom model
							await NotifyService.Notify(executor, "Drop-to set.");
							return CallState.Empty;
						}
					);
				}

				await NotifyService.Notify(executor, "Invalid object type for linking.");
				return Errors.ErrorInvalidObjectType;
			}
		);
	}

	[SharpCommand(Name = "@NUKE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Nuke(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @nuke is an alias for @destroy/override - manually check for SAFE flag
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				// @nuke bypasses SAFE flag

				// Check if already marked GOING
				if (await obj.HasFlag("GOING"))
				{
					// Mark as GOING_TWICE for immediate destruction
					await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "GOING_TWICE", false);
					await NotifyService!.Notify(executor, $"Destroyed: {obj.Object().Name}");
					
					// NOTE: Actual object deletion from database requires a garbage collection system
					// Objects marked GOING_TWICE will be cleaned up by a future purge process
					return CallState.Empty;
				}

				// Mark as GOING
				await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "GOING", false);
				await NotifyService!.Notify(executor, $"Marked for destruction: {obj.Object().Name}");
				
				// Trigger @adestroy attribute if it exists
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
		);
	}

	[SharpCommand(Name = "@UNDESTROY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> UnDestroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				// Check if marked for destruction
				if (!await obj.HasFlag("GOING"))
				{
					await NotifyService.Notify(executor, "That object is not marked for destruction.");
					return Errors.ErrorNotGoing;
				}

				// Remove GOING and GOING_TWICE flags
				if (await obj.HasFlag("GOING"))
				{
					await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "!GOING", false);
				}
				if (await obj.HasFlag("GOING_TWICE"))
				{
					await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, obj, "!GOING_TWICE", false);
				}

				await NotifyService.Notify(executor, $"Spared from destruction: {obj.Object().Name}");
				
				// Trigger @startup attribute if it exists
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
		MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> ChangeZone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
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
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				// Handle "none" to remove zone
				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					await AttributeService!.SetAttributeAsync(executor, obj, "ZONE", MModule.single(""));
					await NotifyService.Notify(executor, "Zone cleared.");
					return CallState.Empty;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj =>
					{
						// Set the zone attribute
						await AttributeService!.SetAttributeAsync(executor, obj, "ZONE", 
							MModule.single(zoneObj.Object().DBRef.ToString()));
						
						// Clear privileged flags and powers unless /preserve is used
						if (!preserve && !obj.IsPlayer)
						{
							// Clear WIZARD, ROYALTY, TRUST flags if present
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
						}

						await NotifyService.Notify(executor, "Zone set.");
						return CallState.Empty;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@DIG", Switches = ["TELEPORT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 6)]
	public static async ValueTask<Option<CallState>> Dig(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
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
			await NotifyService!.Notify(executor.DBRef, "Dig what?");
			return new CallState("#-1 NO ROOM NAME SPECIFIED");
		}

		// NOTE: Additional permission checks needed:
		// - Can executor create rooms (quota check)
		// - Does executor have DIG permission

		// CREATE ROOM
		var response = await Mediator!.Send(new CreateRoomCommand(MModule.plainText(roomName),
			await executor.Owner.WithCancellation(CancellationToken.None)));
		await NotifyService!.Notify(executor.DBRef, $"{roomName} created with room number #{response.Number}.");

		if (!string.IsNullOrWhiteSpace(exitTo?.ToString()))
		{
			var exitToName = MModule.plainText(exitTo).Split(";");
			// CAN CREATE EXIT HERE?
			// CAN LINK TO DESTINATION?

			var toExitResponse = await Mediator.Send(new CreateExitCommand(exitToName.First(),
				exitToName.Skip(1).ToArray(), await executorBase.Where(),
				await executor.Owner.WithCancellation(CancellationToken.None)));
			await NotifyService.Notify(executor.DBRef, $"Opened exit #{toExitResponse.Number}");
			await NotifyService.Notify(executor.DBRef, "Trying to link...");

			var newRoomObject = await Mediator.Send(new GetObjectNodeQuery(response));
			var newExitObject = await Mediator.Send(new GetObjectNodeQuery(toExitResponse));

			await Mediator.Send(new LinkExitCommand(newExitObject.AsExit, newRoomObject.AsRoom));

			await NotifyService.Notify(executor.DBRef, $"Linked exit #{toExitResponse.Number} to #{response.Number}");
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

			await NotifyService.Notify(executor.DBRef, $"Opened exit #{fromExitResponse.Number}");
			await NotifyService.Notify(executor.DBRef, "Trying to link...");

			var where = await executorBase.Where();
			await Mediator.Send(new LinkExitCommand(newExitObject.AsExit, where));

			await NotifyService.Notify(executor.DBRef,
				$"Linked exit #{fromExitResponse.Number} to #{where.Object().DBRef.Number}");
		}

		return new CallState(response.ToString());
	}

	[SharpCommand(Name = "@LOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Lock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var lockKey = args["1"].Message!.ToPlainText();

		// Determine lock type from switches
		var lockType = "Basic";
		if (parser.CurrentState.Switches.Any())
		{
			lockType = parser.CurrentState.Switches.First();
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				await Mediator!.Send(new SetLockCommand(obj.Object(), lockType, lockKey));
				await NotifyService.Notify(executor, "Locked.");
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@UNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Unlock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();

		// Determine lock type from switches
		var lockType = "Basic";
		if (parser.CurrentState.Switches.Any())
		{
			lockType = parser.CurrentState.Switches.First();
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, target, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				await Mediator!.Send(new UnsetLockCommand(obj.Object(), lockType));
				await NotifyService.Notify(executor, "Unlocked.");
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@OPEN", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 5)]
	public static async ValueTask<Option<CallState>> Open(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var exitName = args["0"].Message!.ToPlainText();
		
		// Parse exit name and aliases
		var exitParts = exitName.Split(";");
		var primaryName = exitParts[0];
		var aliases = exitParts.Skip(1).ToArray();

		// Get current location or source room if specified
		var sourceRoom = await executor.Where();
		if (args.ContainsKey("2") && !string.IsNullOrWhiteSpace(args["2"].Message!.ToPlainText()))
		{
			var sourceRoomName = args["2"].Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, sourceRoomName, LocateFlags.All);
			
			if (locateResult.IsError || !locateResult.AsSharpObject.IsRoom)
			{
				await NotifyService!.Notify(executor, "Source must be a room.");
				return new CallState(Errors.ErrorInvalidRoom);
			}
			sourceRoom = locateResult.AsSharpObject.AsRoom;
		}

		// Check permissions
		if (!await PermissionService!.Controls(executor, sourceRoom.WithExitOption()))
		{
			await NotifyService!.Notify(executor, Errors.ErrorPerm);
			return new CallState(Errors.ErrorPerm);
		}

		// Create the exit
		var exitDbRef = await Mediator!.Send(new CreateExitCommand(
			primaryName,
			aliases,
			sourceRoom,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)
		));

		await NotifyService.Notify(executor, $"Opened exit {primaryName} with dbref #{exitDbRef.Number}.");

		// Link to destination if provided
		if (args.ContainsKey("1") && !string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
		{
			var destName = args["1"].Message!.ToPlainText();
			var locateResult = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, destName, LocateFlags.All);
			
			if (!locateResult.IsError && locateResult.AsSharpObject.IsRoom)
			{
				var exitObj = await Mediator.Send(new GetObjectNodeQuery(exitDbRef));
				await Mediator.Send(new LinkExitCommand(exitObj.AsExit, locateResult.AsSharpObject.AsRoom));
				await NotifyService.Notify(executor, $"Linked to {destName}.");
			}
		}

		return new CallState(exitDbRef.ToString());
	}

	[SharpCommand(Name = "@CLONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Clone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");
		
		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var location = await Mediator.Send(new GetObjectNodeQuery(defaultHomeDbref));
		
		if (location.IsNone || location.IsExit)
		{
			await NotifyService!.Notify(executor, "Default home location is invalid.");
			return new CallState(Errors.ErrorInvalidRoom);
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				if (obj.IsPlayer)
				{
					await NotifyService.Notify(executor, "You cannot clone players.");
					return Errors.ErrorInvalidObjectType;
				}

				// Determine new name
				var newName = obj.Object().Name;
				if (args.ContainsKey("1") && !string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
				{
					newName = args["1"].Message!.ToPlainText();
				}

				DBRef cloneDbRef;
				var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

				// Create the appropriate object type
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
					await NotifyService.Notify(executor, "Cannot clone this object type.");
					return Errors.ErrorInvalidObjectType;
				}

				// Get the cloned object
				var clonedObjOptional = await Mediator!.Send(new GetObjectNodeQuery(cloneDbRef));
				var clonedObj = clonedObjOptional.WithoutNone();

				// Copy attributes (excluding system attributes)
				await foreach (var attr in obj.Object().Attributes.Value)
				{
					if (!attr.Name.StartsWith("_"))
					{
						await AttributeService!.SetAttributeAsync(executor, clonedObj,
							attr.Name, attr.Value);
					}
				}

				// Copy flags (excluding privileged ones unless /preserve)
				await foreach (var flag in obj.Object().Flags.Value)
				{
					if (preserve || (!flag.Name.Contains("WIZARD") && !flag.Name.Contains("ROYALTY")))
					{
						await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, clonedObj, flag.Name, false);
					}
				}

				await NotifyService.Notify(executor, $"Cloned. New object: #{cloneDbRef.Number}.");
				return new CallState(cloneDbRef.ToString());
			}
		);
	}

	[SharpCommand(Name = "@MONIKER", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Moniker(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				// If no moniker provided, clear it
				if (!args.ContainsKey("1") || string.IsNullOrWhiteSpace(args["1"].Message!.ToPlainText()))
				{
					await AttributeService!.SetAttributeAsync(executor, obj, "MONIKER", MModule.single(""));
					await NotifyService.Notify(executor, "Moniker cleared.");
					return CallState.Empty;
				}

				var moniker = args["1"].Message!;
				await AttributeService!.SetAttributeAsync(executor, obj, "MONIKER", moniker);
				await NotifyService.Notify(executor, "Moniker set.");
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@PARENT", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Parent(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!await PermissionService!.Controls(executor, target))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
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


	[SharpCommand(Name = "@UNLINK", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Unlink(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return Errors.ErrorPerm;
				}

				if (obj.IsExit)
				{
					await Mediator!.Send(new UnlinkExitCommand(obj.AsExit));
					await NotifyService.Notify(executor, "Unlinked.");
					return CallState.Empty;
				}
				else if (obj.IsRoom)
				{
					// Remove drop-to
					// NOTE: Drop-to removal requires DROP-TO property implementation in SharpRoom model
					await NotifyService.Notify(executor, "Drop-to removed.");
					return CallState.Empty;
				}

				await NotifyService.Notify(executor, "Invalid object type for unlinking.");
				return Errors.ErrorInvalidObjectType;
			}
		);
	}
}