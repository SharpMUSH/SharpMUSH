using FSharpPlus.Internals;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.ComponentModel.DataAnnotations;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using Errors = SharpMUSH.Library.Definitions.Errors;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@RECYCLE", Switches = ["OVERRIDE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Recycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// TODO: Using the Cost is not implemented.
	/// </remarks>
	[SharpCommand(Name = "@CREATE", Behavior = CB.Default, MinArgs = 1, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Create(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Validate Name 
		var args = parser.CurrentState.Arguments;
		var name = MModule.plainText(args["0"].Message!);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var thing = await Mediator!.Send(new CreateThingCommand(name,
			await executor.Where(),
			await executor.Object()
				.Owner.WithCancellation(CancellationToken.None)));

		return new CallState(thing.ToString());
	}

	[SharpCommand(Name = "@FIRSTEXIT", Switches = [], Behavior = CB.Default | CB.Args, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> FirstExit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;

		await foreach (var exit in args.ToAsyncEnumerable())
		{
			// TODO: CONTROL check -- you cannot modify exits in that room.
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, exit.Value.Message!.ToPlainText(),
				LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsPreference,
				async o =>
				{
					var oldData = o.AsExit;
					var oldLocation = await oldData.Location.WithCancellation(CancellationToken.None);
					await Mediator!.Send(new UnlinkExitCommand(oldData));
					await Mediator!.Send(new LinkExitCommand(oldData, oldLocation));
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
			async found =>
			{
				if (!await PermissionService!.Controls(executor, found))
				{
					return Errors.ErrorPerm;
				}

				switch (found)
				{
					case { IsThing: true } or { IsRoom: true }:
						await Mediator!.Send(new SetNameCommand(found, name));
						return found.Object().DBRef;

					case { IsPlayer: true }:
						var tryFindPlayerByName = (await Mediator!.Send(new GetPlayerQuery(name.ToPlainText()))).ToArray();
						if (tryFindPlayerByName.Any(x =>
							    x.Object.Name.Equals(name.ToPlainText(), StringComparison.InvariantCultureIgnoreCase)))
						{
							return "#-1 PLAYER NAME ALREADY IN USE.";
						}

						var playerSplit = MModule.split(";", name);

						await Mediator!.Send(new SetNameCommand(found, playerSplit.First()));

						if (playerSplit.Length <= 1)
						{
							return found.Object().DBRef;
						}

						var aliases = playerSplit.Skip(1).Select(x => x.ToPlainText()).ToArray();

						if (tryFindPlayerByName
						    .SelectMany(x => x.Aliases ?? [])
						    .Intersect(aliases, StringComparer.InvariantCultureIgnoreCase)
						    .Any())
						{
							return "#-1 PLAYER ALIAS ALREADY IN USE.";
						}

						await AttributeService!.SetAttributeAsync(executor, found, "ALIAS",
							MModule.multipleWithDelimiter(MModule.single(";"), aliases.Select(MModule.single)));

						return found.Object().DBRef;

					default:
						var split = MModule.split(";", name);
						await Mediator!.Send(new SetNameCommand(found, split.First()));
						if (split.Length > 1)
						{
							await AttributeService!.SetAttributeAsync(executor, found, "ALIAS",
								MModule.multipleWithDelimiter(MModule.single(";"), split.Skip(1)));
						}

						return found.Object().DBRef;
				}
			}
		);
	}

	[SharpCommand(Name = "@SET", Behavior = CB.RSArgs | CB.EqSplit, MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> SetCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(args["0"].Message!));
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();

		if (!split.TryPickT0(out var details, out var _))
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
			var plainFlag = MModule.plainText(flag);
			var unset = plainFlag.StartsWith('!');
			plainFlag = unset ? plainFlag[1..] : plainFlag;
			// TODO: Permission Check for each flag.
			// Probably should have a service for this?

			var realFlag = await Mediator!.Send(new GetObjectFlagQuery(plainFlag));

			if (realFlag is null) continue;

			await NotifyService!.Notify(executor, $"Flag: {realFlag} Set.");

			// Set Flag	
			if (unset)
			{
				await Mediator!.Send(new UnsetObjectFlagCommand(realLocated, realFlag));
			}
			else
			{
				await Mediator!.Send(new SetObjectFlagCommand(realLocated, realFlag));
			}
		}

		return CallState.Empty;
	}


	[SharpCommand(Name = "@CHOWN", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ChangeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DESTROY", Switches = ["OVERRIDE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Destroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LINK", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Link(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NUKE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Nuke(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UNDESTROY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnDestroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CHZONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ChangeZone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DIG", Switches = ["TELEPORT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 1, MaxArgs = 6)]
	public static async ValueTask<Option<CallState>> Dig(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Fix verbiage for Notify
		/*
		  Room Name created with room number 1255.
			Opened exit #1254
			Trying to link...
			Linked exit #1254 to #1255
			Opened exit #1256
			Trying to link...
			Linked exit #1256 to #452
		 */

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

		// TODO: Permissions
		// CAN DIG?

		// CREATE ROOM
		var response = await Mediator!.Send(new CreateRoomCommand(MModule.plainText(roomName),
			await executor.Owner.WithCancellation(CancellationToken.None)));
		await NotifyService!.Notify(executor.DBRef, $"{roomName} created with room number #{response.Number}.");

		if (!string.IsNullOrWhiteSpace(exitTo?.ToString()))
		{
			var exitToName = MModule.plainText(exitTo).Split(";");
			// CAN CREATE EXIT HERE?
			// CAN LINK TO DESTINATION?

			var toExitResponse = await Mediator!.Send(new CreateExitCommand(exitToName.First(),
				exitToName.Skip(1).ToArray(), await executorBase.Where(),
				await executor.Owner.WithCancellation(CancellationToken.None)));
			await NotifyService!.Notify(executor.DBRef, $"Opened exit #{toExitResponse.Number}");
			await NotifyService!.Notify(executor.DBRef, "Trying to link...");

			var newRoomObject = await Mediator!.Send(new GetObjectNodeQuery(response));
			var newExitObject = await Mediator!.Send(new GetObjectNodeQuery(toExitResponse));

			await Mediator!.Send(new LinkExitCommand(newExitObject.AsExit, newRoomObject.AsRoom));

			await NotifyService!.Notify(executor.DBRef, $"Linked exit #{toExitResponse.Number} to #{response.Number}");
		}

		if (!string.IsNullOrWhiteSpace(exitFrom?.ToString()))
		{
			// CAN CREATE EXIT THERE?
			// CAN LINK BACK TO CURRENT ROOM?

			var exitFromName = MModule.plainText(exitFrom).Split(";");
			var newRoomObject = await Mediator!.Send(new GetObjectNodeQuery(response));

			var fromExitResponse = await Mediator!.Send(new CreateExitCommand(exitFromName.First(),
				exitFromName.Skip(1).ToArray(), newRoomObject.AsRoom,
				await executor.Owner.WithCancellation(CancellationToken.None)));
			var newExitObject = await Mediator!.Send(new GetObjectNodeQuery(fromExitResponse));

			await NotifyService!.Notify(executor.DBRef, $"Opened exit #{fromExitResponse.Number}");
			await NotifyService!.Notify(executor.DBRef, "Trying to link...");

			var where = await executorBase.Where();
			await Mediator!.Send(new LinkExitCommand(newExitObject.AsExit, where));

			await NotifyService!.Notify(executor.DBRef,
				$"Linked exit #{fromExitResponse.Number} to #{where.Object().DBRef.Number}");
		}

		return new CallState(response.ToString());
	}

	[SharpCommand(Name = "@LOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Lock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Unlock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@OPEN", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Open(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CLONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Clone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MONIKER", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Moniker(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PARENT", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Parent(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		/*  @parent <object>[=<parent>]
	
		This command sets the parent of <object> to <parent>. If no <parent> is given, or <parent> is "none", <object>'s parent is cleared. 
		You must control <object>, and must either control <parent> or it must be set LINK_OK and you must pass its @lock/parent.*/

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
				async o =>
				{
					if(args.Count == 1)
					{
						await Mediator!.Send(new UnsetObjectParentCommand(o));
					}

					// TODO: Ensure there's no parent loop, or would not create a loop.

					return CallState.Empty;
				}
			);
	}

	[SharpCommand(Name = "@UNLINK", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Unlink(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}