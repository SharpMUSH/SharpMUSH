﻿using System.Collections.Immutable;
using MoreLinq.Extensions;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using System.Drawing;
using SharpMUSH.Library.Services;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	[SharpCommand(Name = "@@", Switches = [], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static ValueTask<Option<CallState>> At(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		return ValueTask.FromResult(new Option<CallState>(CallState.Empty));
	}

	[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (args.IsEmpty)
		{
			return new None();
		}

		var notification = args["0"].Message!.ToString();
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		await parser.NotifyService.Notify(enactor, notification);

		return new None();
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		await parser.NotifyService.Notify(enactor, "Huh?  (Type \"help\" for help.)");
		return new None();
	}

	[SharpCommand(Name = "@DOLIST", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY"])]
	public static async ValueTask<Option<CallState>> DoList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();

		if (parser.CurrentState.Arguments.Count < 2)
		{
			await parser.NotifyService.Notify(enactor, "What do you want to do with the list?");
			return new None();
		}

		var list = MModule.split(" ", parser.CurrentState.Arguments["0"].Message!);

		var wrappedIteration = new IterationWrapper<MString> { Value = MModule.empty() };
		parser.CurrentState.IterationRegisters.Push(wrappedIteration);
		var command = parser.CurrentState.Arguments["1"].Message!;

		var lastCallState = CallState.Empty;
		foreach (var item in list)
		{
			wrappedIteration.Value = item!;
			wrappedIteration.Iteration++;
			// TODO: This should not need parsing each time.
			// Just Evaluation by getting the Context and Visiting the Children multiple times.
			lastCallState = await parser.CommandListParse(command);
		}

		parser.CurrentState.IterationRegisters.TryPop(out _);

		return lastCallState!;
	}

	[SharpCommand(Name = "LOOK", Switches = ["OUTSIDE", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Consult CONFORMAT, DESCFORMAT, INAMEFORMAT, NAMEFORMAT, etc.

		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		AnyOptionalSharpObject viewing = new None();

		if (args.Count == 1)
		{
			var locate = await parser.LocateService.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				args["0"]!.Message!.ToString(),
				Library.Services.LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}
		else
		{
			viewing = (await parser.Mediator.Send(new GetCertainLocationQuery(enactor.Id()!))).WithExitOption()
				.WithNoneOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var contents = viewing.IsExit
			? []
			: (await parser.Mediator.Send(new GetContentsQuery(viewing.WithoutNone().AsContainer)))!.ToList();
		var viewingObject = viewing.Object()!;

		var name = viewingObject.Name;
		var contentKeys = contents.Where(x => x.IsPlayer || x.IsThing).Select(x => x.Object().Name).ToList();
		var exitKeys = contents.Where(x => x.IsExit).Select(x => x.Object().Name).ToList();
		var description = (await parser.AttributeService.GetAttributeAsync(enactor, viewing.Known(), "DESCRIBE",
				IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Value,
				_ => MModule.single("There is nothing to see here"),
				_ => MModule.empty());

		// TODO: Pass value into NAMEFORMAT
		await parser.NotifyService.Notify(enactor,
			$"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(name))}" +
			$"(#{viewingObject.DBRef.Number}{string.Join(string.Empty, viewingObject.Flags.Value.Select(x => x.Symbol))})");
		// TODO: Pass value into DESCFORMAT
		await parser.NotifyService.Notify(enactor, description.ToString());
		// parser.NotifyService.Notify(enactor, $"Location: {location}");
		// TODO: Pass value into CONFORMAT
		await parser.NotifyService.Notify(enactor, $"Contents: {string.Join(Environment.NewLine, contentKeys)}");
		// TODO: Pass value into EXITFORMAT
		await parser.NotifyService.Notify(enactor,
			$"Exits: {string.Join(Environment.NewLine, string.Join(", ", exitKeys))}");

		return new CallState(viewingObject.DBRef.ToString());
	}

	[SharpCommand(Name = "EXAMINE", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		AnyOptionalSharpObject viewing = new None();

		// TODO: Implement the version of this command that takes an attribute pattern!
		if (args.Count == 1)
		{
			var locate = await parser.LocateService.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				args["0"]!.Message!.ToString(),
				Library.Services.LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}
		else
		{
			viewing = (await parser.Mediator.Send(new GetLocationQuery(enactor.Object().DBRef, 1))).WithExitOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var contents = viewing.IsExit ? [] : await parser.Mediator.Send(new GetContentsQuery(viewing.Known().AsContainer));

		var obj = viewing.Object()!;
		var ownerObj = obj.Owner.Value.Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var location = obj.Key;
		var contentKeys = contents!.Select(x => x.Object()!.Name);
		var exitKeys = (await parser.Mediator.Send(new GetExitsQuery(obj.DBRef)))?.FirstOrDefault();
		var description = (await parser.AttributeService.GetAttributeAsync(enactor, viewing.Known(), "DESCRIBE",
				Library.Services.IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Value,
				none => MModule.single("There is nothing to see here"),
				error => MModule.empty());

		await parser.NotifyService.Notify(enactor, $"{name.Hilight()}" +
		                                           $"(#{obj.DBRef.Number}{string.Join(string.Empty, obj.Flags.Value.Select(x => x.Symbol))})");
		await parser.NotifyService.Notify(enactor,
			$"Type: {obj.Type} Flags: {string.Join(" ", obj.Flags.Value.Select(x => x.Name))}");
		await parser.NotifyService.Notify(enactor, description.ToString());
		await parser.NotifyService.Notify(enactor, $"Owner: {ownerName.Hilight()}" +
		                                           $"(#{obj.DBRef.Number}{string.Join(string.Empty, ownerObj.Flags.Value.Select(x => x.Symbol))})");
		// TODO: Zone & Money
		await parser.NotifyService.Notify(enactor, $"Parent: {obj.Parent.Value?.Name ?? "*NOTHING*"}");
		// TODO: LOCK LIST
		await parser.NotifyService.Notify(enactor, $"Powers: {string.Join(" ", obj.Powers.Value.Select(x => x.Name))}");
		// TODO: Channels
		// TODO: Warnings Checked

		// TODO: Match proper date format: Mon Feb 26 18:05:10 2007
		await parser.NotifyService.Notify(enactor,
			$"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F}");

		var atrs = await parser.AttributeService.GetVisibleAttributesAsync(enactor, viewing.Known());

		if (atrs.IsAttribute)
		{
			foreach (var attr in atrs.AsAttributes)
			{
				// TODO: Symbols for Flags. Flags are not just strings!
				await parser.NotifyService.Notify(enactor,
					MModule.concat(
						$"{attr.Name} [#{attr.Owner.Value.Object.DBRef.Number}]: "
							.Hilight()
						, attr.Value));
			}
		}

		// TODO: Proper carry format.
		await parser.NotifyService.Notify(enactor, $"Contents: {Environment.NewLine}" +
		                                           $"{string.Join(Environment.NewLine, contentKeys)}");

		if (!viewing.IsRoom)
		{
			// TODO: Proper Format.
			await parser.NotifyService.Notify(enactor, $"Home: {viewing.Known().MinusRoom().Home().Object().Name}");
			await parser.NotifyService.Notify(enactor, $"Location: {viewing.Known().MinusRoom().Location().Object().Name}");
		}

		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "@PEMIT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> PEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (args.Count < 2)
		{
			return new CallState(string.Empty);
		}

		var notification = args["1"].Message!.ToString();
		var targetListText = MModule.plainText(args["0"].Message!);
		var nameListTargets = Functions.Functions.NameList(targetListText);

		var enactor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();

		foreach (var target in nameListTargets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var locateTarget = await parser.LocateService.LocateAndNotifyIfInvalid(parser, enactor, enactor, targetString,
				Library.Services.LocateFlags.All);

			if (locateTarget.IsValid())
			{
				await parser.NotifyService.Notify(locateTarget.WithoutError().Known(), notification);
			}
		}

		return new None();
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Goto(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		var enactorObj = (await parser.CurrentState.EnactorObject(parser.Mediator)).Known();
		if (args.Count < 1)
		{
			await parser.NotifyService.Notify(enactor, "You can't go that way.");
			return CallState.Empty;
		}

		var exit = await parser.LocateService.LocateAndNotifyIfInvalid(
			parser,
			enactorObj,
			enactorObj,
			args["0"]!.Message!.ToString(),
			LocateFlags.ExitsInTheRoomOfLooker
			| LocateFlags.EnglishStyleMatching
			| LocateFlags.ExitsPreference
			| LocateFlags.OnlyMatchTypePreference
			| LocateFlags.FailIfNotPreferred);

		if (!exit.IsValid())
		{
			return CallState.Empty;
		}

		var exitObj = exit.WithoutError().WithoutNone().AsExit;
		// TODO: Check if the exit has a destination attribute.
		var destination = exitObj.Home.Value;

		if (!parser.PermissionService.CanGoto(enactorObj, exitObj, exitObj.Home.Value))
		{
			await parser.NotifyService.Notify(enactor, "You can't go that way.");
			return CallState.Empty;
		}

		await parser.Mediator.Send(new MoveObjectCommand(enactorObj.AsContent, destination));

		return new CallState(destination.ToString());
	}


	[SharpCommand(Name = "@TELEPORT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2,
		Switches = ["LIST", "INSIDE", "QUIET"])]
	public static async ValueTask<Option<CallState>> Teleport(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();

		// If /list - Arg0 can contain multiple SharpObject
		// If not, Arg0 is a singular object
		// Arg0 must only contain SharpContents (Validation)
		// If Arg1 does not exist, Arg0 is the Destination for the Enactor.
		// Otherwise, Arg1 is the Destination for the Arg0.

		var enactorObj = (await parser.CurrentState.EnactorObject(parser.Mediator)).Known();
		var destinationString = MModule.plainText(args.Count == 1 ? args["0"].Message : args["1"].Message);
		var toTeleport = MModule.plainText(args.Count == 1 ? MModule.single(enactor.ToString()) : args["0"].Message);

		var isList = parser.CurrentState.Switches.Contains("list");

		var toTeleportList = Enumerable.Empty<OneOf<DBRef, string>>();
		if (isList)
		{
			Functions.Functions.NameList(toTeleport);
		}
		else
		{
			var isDBRef = DBRef.TryParse(toTeleport, out var objToTeleport);
			toTeleportList = [isDBRef ? objToTeleport!.Value : toTeleport];
		}

		var toTeleportStringList = toTeleportList.Select(x => x.ToString());

		var destination = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
			enactorObj,
			enactorObj,
			destinationString,
			Library.Services.LocateFlags.All);

		if (!destination.IsValid())
		{
			await parser.NotifyService.Notify(enactor, "You can't go that way.");
			return CallState.Empty;
		}

		var validDestination = destination.WithoutError().WithoutNone();

		if (validDestination.IsExit)
		{
			// TODO: Implement Teleporting through an Exit.
			return CallState.Empty;
		}

		var destinationContainer = validDestination.AsContainer;

		foreach (var obj in toTeleportStringList)
		{
			var locateTarget = await parser.LocateService.LocateAndNotifyIfInvalid(parser, enactorObj, enactorObj, obj,
				Library.Services.LocateFlags.All);
			if (!locateTarget.IsValid() || locateTarget.IsRoom)
			{
				await parser.NotifyService.Notify(enactor, Errors.ErrorNotVisible);
				continue;
			}

			var target = locateTarget.WithoutError().WithoutNone();
			var targetContent = target.AsContent;
			if (!parser.PermissionService.Controls(enactorObj, target))
			{
				await parser.NotifyService.Notify(enactor, Errors.ErrorCannotTeleport);
				continue;
			}

			await parser.Mediator.Send(new MoveObjectCommand(targetContent, destinationContainer));
			// TODO: Notify the target that they have been teleported - if Quiet switch is not present.
			// TODO: Evaluate room verbs upon teleportation.
			// TODO: If the target is a player, force a LOOK

			// CONSIDER:
			// There are two ways to move a player: GOTO (exits) and TELEPORT (directly).
			// Rooms evaluate OENTER either way.
			// Is this a reason to make this into a 'move service' just for the movement itself?
			// Or just a common static function?
		}

		return new CallState(destination.ToString());
	}

	[SharpCommand(Name = "@CEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FIND", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> FIND(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@HALT", Switches = ["ALL", "NOEVAL", "PID"], Behavior = CB.Default | CB.EqSplit | CB.RSBrace,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> HALT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NOTIFY", Switches = ["ALL", "ANY", "SETQ"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NOTIFY(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSPROMPT", Switches = ["SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSPROMPT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SCAN", Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SCAN(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SWITCH",
		Switches = ["NOTIFY", "FIRST", "ALL", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SWITCH(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WAIT", Switches = ["PID", "UNTIL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WAIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@COMMAND",
		Switches =
		[
			"ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE",
			"DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"
		], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> COMMAND(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DRAIN", Switches = ["ALL", "ANY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> DRAIN(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FORCE", Switches = ["NOEVAL", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSBrace, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> FORCE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@IFELSE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> IFELSE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSEMIT", Switches = ["ROOM", "NOEVAL", "SILENT"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSREMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSREMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PROMPT", Switches = ["SILENT", "NOISY", "NOEVAL", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> PROMPT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SEARCH", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SEARCH(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WHEREIS", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WHEREIS(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@BREAK", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> BREAK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CONFIG", Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CONFIG(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@EDIT", Switches = ["FIRST", "CHECK", "QUIET", "REGEXP", "NOCASE", "ALL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> EDIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FUNCTION",
		Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> FUNCTION(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSLEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSLEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSZEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PS", Switches = ["ALL", "SUMMARY", "COUNT", "QUICK", "DEBUG"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> PS(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SELECT",
		Switches = ["NOTIFY", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SELECT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@TRIGGER",
		Switches = ["CLEARREGS", "SPOOF", "INLINE", "NOBREAK", "LOCALIZE", "INPLACE", "MATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> TRIGGER(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ZEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CHANNEL",
		Switches =
		[
			"LIST", "ADD", "DELETE", "RENAME", "MOGRIFIER", "NAME", "PRIVS", "QUIET", "DECOMPILE", "DESCRIBE", "CHOWN",
			"WIPE", "MUTE", "UNMUTE", "GAG", "UNGAG", "HIDE", "UNHIDE", "WHAT", "TITLE", "BRIEF", "RECALL", "BUFFER",
			"COMBINE", "UNCOMBINE", "ON", "JOIN", "OFF", "LEAVE", "WHO"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CHANNEL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DECOMPILE", Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> DECOMPILE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> EMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LISTMOTD", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LISTMOTD(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSOEMIT", Switches = ["NOEVAL"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSOEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@OEMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> OEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@REMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> REMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@STATS", Switches = ["CHUNKS", "FREESPACE", "PAGING", "REGIONS", "TABLES", "FLAGS"],
		Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> STATS(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@VERB", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> VERB(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CHAT", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CHAT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ENTRANCES", Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ENTRANCES(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@GREP", Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> GREP(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@INCLUDE", Switches = ["LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> INCLUDE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MAIL",
		Switches =
		[
			"NOEVAL", "NOSIG", "STATS", "CSTATS", "DSTATS", "FSTATS", "DEBUG", "NUKE", "FOLDERS", "UNFOLDER", "LIST", "READ",
			"UNREAD", "CLEAR", "UNCLEAR", "STATUS", "PURGE", "FILE", "TAG", "UNTAG", "FWD", "FORWARD", "SEND", "SILENT",
			"URGENT", "REVIEW", "RETRACT"
		], Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Mail(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message;
		var arg1 = parser.CurrentState.Arguments["1"].Message;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var caller = (await parser.CurrentState.CallerObject(parser.Mediator)).Known();
		string[] sendSwitches = ["SEND", "URGENT", "NOSIG", "SILENT", "NOEVAL"];

		if (switches.Except(sendSwitches).Any() && switches.Length > 1)
		{
			await parser.NotifyService.Notify(executor, "Error: Too many switches passed to @mail.", caller);
			return new CallState(Errors.ErrorTooManyRegs);
		}

		if (!switches.Contains("NOEVAL"))
		{
			arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();
			arg1 = await parser.CurrentState.Arguments["1"].ParsedMessage();
		}

		if (switches.Contains("FOLDER"))
		{
			// Mail Folder
		}
		
		if (switches.Contains("UNFOLDER"))
		{
			// Mail Folder
		}
		
		if (switches.Contains("FILE"))
		{
			// Mail Folder
		}

		if (switches.Contains("CLEAR"))
		{
			// Clear items in mail list 
		}
		
		if (switches.Contains("UNCLEAR"))
		{
			// Clear items in mail list 
		}
		
		if (switches.Contains("TAG"))
		{
			// Clear items in mail list 
		}
		
		if (switches.Contains("UNTAG"))
		{
			// Clear items in mail list 
		}
		
		if (switches.Contains("UNREAD"))
		{
			// Clear items in mail list 
		}
		
		if (switches.Contains("STATUS"))
		{
			// Clear items in mail list 
		}
		
		if (switches.Contains("CSTATS"))
		{
			// Mail Stats
		}

		if (switches.Contains("STATS"))
		{
			// Mail Stats on Player
		}

		if (switches.Contains("DSTATS"))
		{
			// Mail Stats on Player with Read/Unread
		}

		
		if (switches.Contains("FSTATS"))
		{
			// Mail Stats on Player with Read/Unread with Space Usage
		}

		if (switches.Contains("DEBUG"))
		{
			// Mail Database Sanity Check
		}
		
		if(switches.Contains("NUKE"))
		{
			// Erase all Mail sent to a player
		}

		if (switches.Contains("REVIEW"))
		{
			// List Mail sent from person to User
		}

		if (switches.Contains("RETRACT"))
		{
			// Retract a Mail from other user's inbox if unread 
		}
		
		if (switches.Contains("FWD"))
		{
			// Mail Forward
		}
		
		if (switches.Contains("SEND") || arg1 != null)
		{
			// Mail Send
		}

		if (switches.Contains("READ") || arg1 == null)
		{
			// Mail Read
		}

		if (switches.Contains("LIST") || arg0?.Length == 0 && arg1?.Length == 0)
		{
			// List mail
		}
		
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSPEMIT", Switches = ["LIST", "SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSPEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PASSWORD", Switches = [],
		Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> PASSWORD(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> RESTART(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SWEEP", Switches = ["CONNECTED", "HERE", "INVENTORY", "EXITS"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SWEEP(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@VERSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> VERSION(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@RETRY", Switches = [],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> RETRY(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ASSERT", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ASSERT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ATTRIBUTE",
		Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ATTRIBUTE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SKIP", Switches = ["IFELSE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SKIP(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MESSAGE", Switches = ["NOEVAL", "SPOOF", "NOSPOOF", "REMIT", "OEMIT", "SILENT", "NOISY"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MESSAGE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSCEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NSCEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}