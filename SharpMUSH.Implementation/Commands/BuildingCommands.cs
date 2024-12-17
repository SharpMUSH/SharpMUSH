using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{

	[SharpCommand(Name = "@ATRLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ATRLOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CPATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CPATTR(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MVATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MVATTR(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@RECYCLE", Switches = ["OVERRIDE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> RECYCLE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ATRCHOWN", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ATRCHOWN(IMUSHCodeParser parser, SharpCommandAttribute _2)
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
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();

		var thing = await parser.Mediator.Send(new CreateThingCommand(name, executor.Where, executor.Object()!.Owner.Value));

		return new CallState(thing.ToString());
	}

	[SharpCommand(Name = "@FIRSTEXIT", Switches = [], Behavior = CB.Default | CB.Args, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> FIRSTEXIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NAME", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NAME(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SET", Behavior = CB.RSArgs, MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> SetCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Step 1: Check if the argument[0] could be an Object/Attribute or is just an Object
		// Step 2: Locate Object.
		// Step 3: Check if we have a : in argument[1].
		// Step 4: Either set an attribute, or set an Attribute Flag, or set an Object Flag.

		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(args["0"].Message!));
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();

		if (!split.TryPickT0(out var details, out var _))
		{
			return new CallState("#-1 BAD ARGUMENT FORMAT TO @SET");
		}

		(var dbref, var maybeAttribute) = details;

		var locate = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
			enactor,
			executor,
			dbref,
			Library.Services.LocateFlags.All);

		if (!locate.IsValid())
		{
			return new CallState(locate.IsError ? locate.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var realLocated = locate.WithoutError().WithoutNone();

		// Attr Set Path
		if (maybeAttribute is not null)
		{
			foreach (var flag in MModule.split(" ", args["2"].Message!))
			{
				var plainFlag = MModule.plainText(flag);
				if (plainFlag.StartsWith('!'))
				{
					// TODO: Notify
					await parser.AttributeService.SetAttributeFlagAsync(executor, realLocated, maybeAttribute, plainFlag);
				}
				else
				{
					// TODO: Notify
					await parser.AttributeService.UnsetAttributeFlagAsync(executor, realLocated, maybeAttribute, plainFlag[1..]);
				}
			}

			return new CallState(string.Empty);
		}

		// Attr Flag Path
		var maybeColonLocation = MModule.indexOf(args["1"].Message!, MModule.single(":"));
		if (maybeColonLocation > -1)
		{
			var arg1 = args["1"].Message!;
			var attribute = MModule.substring(0, maybeColonLocation, arg1);
			var content = MModule.substring(maybeColonLocation + 1, MModule.getLength(arg1), arg1);

			var setResult =
				await parser.AttributeService.SetAttributeAsync(executor, realLocated, MModule.plainText(attribute), content);

			await parser.NotifyService.Notify(enactor,
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
			var unset = plainFlag.StartsWith("!");
			plainFlag = unset ? plainFlag[1..] : plainFlag;
			// TODO: Permission Check for each flag.
			// Probably should have a service for this?

			var realFlag = await parser.Mediator.Send(new GetObjectFlagQuery(plainFlag));

			// TODO: Notify
			if (realFlag is null) continue;

			// Set Flag	
			if (unset)
			{
				await parser.Mediator.Send(new UnsetObjectFlagCommand(realLocated, realFlag));
			}
			else
			{
				await parser.Mediator.Send(new SetObjectFlagCommand(realLocated, realFlag));
			}
		}

		throw new NotImplementedException();
	}


	[SharpCommand(Name = "@CHOWN", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CHOWN(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DESTROY", Switches = ["OVERRIDE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> DESTROY(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LINK", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LINK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NUKE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NUKE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UNDESTROY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UNDESTROY(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WIPE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WIPE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CHZONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CHZONE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DIG", Switches = ["TELEPORT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = 6)]
	public static async ValueTask<Option<CallState>> DIG(IMUSHCodeParser parser, SharpCommandAttribute _2)
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
		var executorBase = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var executor = executorBase.Object()!;
		var roomName = parser.CurrentState.Arguments["0"].Message!;
		parser.CurrentState.Arguments.TryGetValue("1", out var exitToCallState);
		parser.CurrentState.Arguments.TryGetValue("2", out var exitFromCallState);
		var exitTo = exitToCallState?.Message;
		var exitFrom = exitFromCallState?.Message;	

		if (string.IsNullOrWhiteSpace(parser.CurrentState.Arguments["0"].Message!.ToString()))
		{
			await parser.NotifyService.Notify(executor.DBRef, "Dig what?");
			return new CallState("#-1 NO ROOM NAME SPECIFIED");
		}

		// TODO: Permissions

		// CREATE ROOM
		var response = await parser.Mediator.Send(new CreateRoomCommand(MModule.plainText(roomName), executor.Owner.Value));
		await parser.NotifyService.Notify(executor.DBRef, $"{roomName} created with room number #{response.Number}.");

		if (!string.IsNullOrWhiteSpace(exitTo?.ToString()))
		{
			var exitToName = MModule.plainText(exitTo!).Split(";");

			var toExitResponse = await parser.Mediator.Send(new CreateExitCommand(exitToName.First(), exitToName.Skip(1).ToArray(), executorBase.Where, executor.Owner.Value));
			await parser.NotifyService.Notify(executor.DBRef, $"Opened exit #{toExitResponse.Number}");
			await parser.NotifyService.Notify(executor.DBRef, "Trying to link...");
			await parser.NotifyService.Notify(executor.DBRef, $"Linked exit #{toExitResponse.Number} to #{response.Number}");
		}

		if (!string.IsNullOrWhiteSpace(exitFrom?.ToString()))
		{
			var exitFromName = MModule.plainText(exitFrom!).Split(";");
			var newRoomObject = await parser.Mediator.Send(new GetObjectNodeQuery(response));

			var fromExitResponse = await parser.Mediator.Send(new CreateExitCommand(exitFromName.First(), exitFromName.Skip(1).ToArray(), newRoomObject.AsRoom, executor.Owner.Value));
			
			await parser.NotifyService.Notify(executor.DBRef, $"Opened exit #{fromExitResponse.Number}");
			await parser.NotifyService.Notify(executor.DBRef, "Trying to link...");
			await parser.NotifyService.Notify(executor.DBRef, $"Linked exit #{fromExitResponse.Number} to #{executorBase.Where.Object().DBRef.Number}");
		}

		return new CallState(response.ToString());
	}

	[SharpCommand(Name = "@LOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UNLOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@OPEN", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> OPEN(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CLONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CLONE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MONIKER", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MONIKER(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PARENT", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> PARENT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UNLINK", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UNLINK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}
