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
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@@", Switches = [], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static ValueTask<Option<CallState>> At(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		return ValueTask.FromResult(new Option<CallState>(CallState.Empty));
	}

	[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		if (parser.CurrentState.Arguments.Count <= 0)
		{
			return new None();
		}

		await parser.NotifyService.Notify(executor, parser.CurrentState.Arguments["0"].Message!.ToString());
		return parser.CurrentState.Arguments["0"];
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		await parser.NotifyService.Notify(executor, "Huh?  (Type \"help\" for help.)");
		return new CallState("#-1 HUH");
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

		var wrappedIteration = new IterationWrapper<MString> { Value = MModule.empty(), Break = false, NoBreak = false, Iteration = 0 };
		parser.CurrentState.IterationRegisters.Push(wrappedIteration);
		var command = parser.CurrentState.Arguments["1"].Message!;

		var lastCallState = CallState.Empty;
		var visitorFunc = parser.CommandListParseVisitor(command);
		foreach (var item in list)
		{
			wrappedIteration.Value = item!;
			wrappedIteration.Iteration++;

			// TODO: This should not need parsing each time.
			// Just Evaluation by getting the Context and Visiting the Children multiple times.
			lastCallState = await visitorFunc();
		}

		parser.CurrentState.IterationRegisters.TryPop(out _);

		return lastCallState!;
	}

	[SharpCommand(Name = "LOOK", Switches = ["OUTSIDE", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Consult CONFORMAT, DESCFORMAT, INAMEFORMAT, NAMEFORMAT, etc.

		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		AnyOptionalSharpObject viewing = new None();

		if (args.Count == 1)
		{
			var locate = await parser.LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				args["0"].Message!.ToString(),
				LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}
		else
		{
			viewing = (await parser.Mediator.Send(new GetCertainLocationQuery(executor.Id()!))).WithExitOption()
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
		var description = (await parser.AttributeService.GetAttributeAsync(executor, viewing.Known(), "DESCRIBE",
				IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Last().Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Last().Value,
				_ => MModule.single("There is nothing to see here"),
				_ => MModule.empty());

		// TODO: Pass value into NAMEFORMAT
		await parser.NotifyService.Notify(executor,
			$"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(name))}" +
			$"(#{viewingObject.DBRef.Number}{string.Join(string.Empty, (await viewingObject.Flags.WithCancellation(CancellationToken.None)).Select(x => x.Symbol))})");
		// TODO: Pass value into DESCFORMAT
		await parser.NotifyService.Notify(executor, description.ToString());
		// parser.NotifyService.Notify(enactor, $"Location: {location}");
		// TODO: Pass value into CONFORMAT
		await parser.NotifyService.Notify(executor, $"Contents: {string.Join(Environment.NewLine, contentKeys)}");
		// TODO: Pass value into EXITFORMAT
		await parser.NotifyService.Notify(executor,
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
				args["0"].Message!.ToString(),
				LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}

		viewing = (await parser.Mediator.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();

		if (viewing.IsNone())
		{
			return new None();
		}

		var contents = viewing.IsExit
			? []
			: await parser.Mediator.Send(new GetContentsQuery(viewing.Known().AsContainer));

		var obj = viewing.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var location = obj.Key;
		var contentKeys = contents!.Select(x => x.Object().Name);
		var exitKeys = await parser.Mediator.Send(new GetExitsQuery(obj.DBRef));
		var description = (await parser.AttributeService.GetAttributeAsync(enactor, viewing.Known(), "DESCRIBE",
				IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Last().Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Last().Value,
				none => MModule.single("There is nothing to see here"),
				error => MModule.empty());

		var objFlags = (await obj.Flags.WithCancellation(CancellationToken.None)).ToArray();
		var ownerObjFlags = (await ownerObj.Flags.WithCancellation(CancellationToken.None)).ToArray();
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);
		var objPowers = await obj.Powers.WithCancellation(CancellationToken.None);

		await parser.NotifyService.Notify(enactor, $"{name.Hilight()}" +
		                                           $"(#{obj.DBRef.Number}{string.Join(string.Empty, objFlags.Select(x => x.Symbol))})");
		await parser.NotifyService.Notify(enactor,
			$"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}");
		await parser.NotifyService.Notify(enactor, description.ToString());
		await parser.NotifyService.Notify(enactor, $"Owner: {ownerName.Hilight()}" +
		                                           $"(#{obj.DBRef.Number}{string.Join(string.Empty, ownerObjFlags.Select(x => x.Symbol))})");
		// TODO: Zone & Money
		await parser.NotifyService.Notify(enactor,
			$"Parent: {(objParent.IsNone ? "*NOTHING*" : objParent.Known.Object().Name)}");
		// TODO: LOCK LIST
		await parser.NotifyService.Notify(enactor, $"Powers: {string.Join(" ", objPowers.Select(x => x.Name))}");
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
						$"{attr.Name} [#{(await attr.Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.Number}]: "
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
			await parser.NotifyService.Notify(enactor, $"Home: {(await viewing.Known().MinusRoom().Home()).Object().Name}");
			await parser.NotifyService.Notify(enactor,
				$"Location: {(await viewing.Known().MinusRoom().Location()).Object().Name}");
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

		var enactor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		foreach (var target in nameListTargets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var locateTarget = await parser.LocateService.LocateAndNotifyIfInvalid(parser, enactor, enactor, targetString,
				LocateFlags.All);

			if (locateTarget.IsValid())
			{
				await parser.NotifyService.Notify(locateTarget.WithoutError().Known(), notification);
			}
		}

		return new None();
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> GoTo(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		if (parser.CurrentState.Arguments.Count == 0)
		{
			await parser.NotifyService.Notify(executor, "You can't go that way.");
			return CallState.Empty;
		}

		var exit = await parser.LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			args["0"].Message!.ToString(),
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
		var destination = await exitObj.Home.WithCancellation(CancellationToken.None);

		if (!await parser.PermissionService.CanGoto(executor, exitObj,
			    await exitObj.Home.WithCancellation(CancellationToken.None)))
		{
			await parser.NotifyService.Notify(executor, "You can't go that way.");
			return CallState.Empty;
		}

		await parser.Mediator.Send(new MoveObjectCommand(executor.AsContent, destination));

		return new CallState(destination.ToString());
	}


	[SharpCommand(Name = "@TELEPORT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2,
		Switches = ["LIST", "INSIDE", "QUIET"])]
	public static async ValueTask<Option<CallState>> Teleport(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		// If /list - Arg0 can contain multiple SharpObject
		// If not, Arg0 is a singular object
		// Arg0 must only contain SharpContents (Validation)
		// If Arg1 does not exist, Arg0 is the Destination for the Enactor.
		// Otherwise, Arg1 is the Destination for the Arg0.

		var destinationString = MModule.plainText(args.Count == 1 ? args["0"].Message : args["1"].Message);
		var toTeleport = MModule.plainText(args.Count == 1 ? MModule.single(executor.ToString()) : args["0"].Message);

		var isList = parser.CurrentState.Switches.Contains("LIST");

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
			executor,
			executor,
			destinationString,
			LocateFlags.All);

		if (!destination.IsValid())
		{
			await parser.NotifyService.Notify(executor, "You can't go that way.");
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
			var locateTarget = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, obj,
				LocateFlags.All);
			if (!locateTarget.IsValid() || locateTarget.IsRoom)
			{
				await parser.NotifyService.Notify(executor, Errors.ErrorNotVisible);
				continue;
			}

			var target = locateTarget.WithoutError().WithoutNone();
			var targetContent = target.AsContent;
			if (!await parser.PermissionService.Controls(executor, target))
			{
				await parser.NotifyService.Notify(executor, Errors.ErrorCannotTeleport);
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
	public static async ValueTask<Option<CallState>> ChannelEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FIND", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Find(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@HALT", Switches = ["ALL", "NOEVAL", "PID"], Behavior = CB.Default | CB.EqSplit | CB.RSBrace,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Halt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NOTIFY", Switches = ["ALL", "ANY", "SETQ", "QUIET"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
		MinArgs = 0, MaxArgs = int.MaxValue)]
	public static async ValueTask<Option<CallState>> Notify(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var switches = parser.CurrentState.Switches.Except(["QUIET"]).ToArray();
		var notifyType = "ANY";
		var args = parser.CurrentState.Arguments;

		if ((parser.CurrentState.Arguments.Count == 0) || string.IsNullOrEmpty(args["0"].Message?.ToPlainText()))
		{
			await parser.NotifyService.Notify(executor, "You must specify an object to use for the semaphore.");
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
			default:
				return new CallState(Errors.ErrorTooManySwitches);
		}

		var objectAndAttribute = HelperFunctions.SplitDBRefAndOptionalAttr(args["0"].Message!.ToPlainText());
		if (objectAndAttribute.IsT1 && objectAndAttribute.AsT1 == false)
		{
			await parser.NotifyService.Notify(executor,
				"You must specify a valid object with an optional valid attribute to use for the semaphore.");
			return new None();
		}

		var (db, maybeAttributeString) = objectAndAttribute.AsT0;
		var maybeObject = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
			db, LocateFlags.All);

		if (maybeObject.IsError) return maybeObject.AsError;
		var objectToNotify = maybeObject.AsSharpObject;

		string attribute = "SEMAPHORE";
		if (!string.IsNullOrEmpty(maybeAttributeString))
		{
			attribute = maybeAttributeString;
		}

		var attributeContents = await parser.AttributeService.GetAttributeAsync(executor, objectToNotify, attribute,
			IAttributeService.AttributeMode.Execute, false);

		if (attributeContents.IsError)
		{
			return new CallState(attributeContents.AsError.Value);
		}

		int oldSemaphoreCount = 0;
		if (attributeContents.IsAttribute &&
		    int.TryParse(attributeContents.AsAttribute.Last().Value.ToPlainText(), out var semaphoreCount))
		{
			oldSemaphoreCount = semaphoreCount;
		}

		var dbRefAttribute = new DbRefAttribute(objectToNotify.Object().DBRef, attribute.Split("`"));

		switch (notifyType)
		{
			// TODO: Handle <number> case.
			case "ANY":
				await parser.Mediator.Send(new NotifySemaphoreRequest(dbRefAttribute, oldSemaphoreCount));
				await parser.AttributeService.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single((oldSemaphoreCount - 1).ToString()));
				break;
			case "ALL":
				await parser.Mediator.Send(new NotifyAllSemaphoreRequest(dbRefAttribute));
				await parser.AttributeService.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single(0.ToString()));
				break;
		}

		// TODO: Handle SETQ case, as it affects the State of an already-queued Job.

		return new None();
	}

	[SharpCommand(Name = "@NSPROMPT", Switches = ["SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> NoSpoofPrompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Noisy, Silent, NoEval

		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var target = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var maybeFound = await parser.LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, target);

		if (maybeFound.IsError)
		{
			return new CallState(maybeFound.AsError);
		}

		var found = maybeFound.AsAnyObject;

		await parser.NotifyService.Prompt(found, message, executor, INotifyService.NotificationType.NSEmit);

		// TODO: Return something better
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SCAN", Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Scan(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var switches = parser.CurrentState.Switches.Any()
			? parser.CurrentState.Switches.ToArray()
			: ["ROOM", "SELF", "ZONE", "GLOBALS"];

		List<string> runningOutput = [];
		// TODO: Permission check on the outputs
		// -> It does NOT scan objects that you do not control and are not set VISUAL.

		if (executor.IsContent && switches.Contains("ROOM"))
		{
			var where = await executor.AsContent.Location();
			var whereContent = await where.Content(parser);

			// notify: Matches on contents of this room:
			var matchedContent =
				await parser.CommandDiscoveryService.MatchUserDefinedCommand(parser,
					whereContent.Select(x => x.WithRoomOption()),
					arg0);

			if (matchedContent.IsSome())
			{
				foreach (var (i, (obj, attr, _)) in matchedContent.AsValue().Index())
				{
					runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
					await parser.NotifyService.Notify(executor,
						$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]");
				}
			}
		}

		if (executor.IsContainer && switches.Contains("SELF"))
		{
			var executorContents = await executor.AsContainer.Content(parser);

			// notify: Matches on carried objects:
			var matchedContent =
				await parser.CommandDiscoveryService.MatchUserDefinedCommand(parser,
					executorContents.Select(x => x.WithRoomOption()),
					arg0);

			if (matchedContent.IsSome())
			{
				foreach (var (i, (obj, attr, _)) in matchedContent.AsValue().Index())
				{
					runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
					await parser.NotifyService.Notify(executor,
						$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]");
				}
			}
		}

		if (switches.Contains("ZONE"))
		{
			// TODO: Zone Match
		}

		if (switches.Contains("GLOBAL"))
		{
			var masterRoom = new DBRef(Convert.ToInt32(parser.Configuration.CurrentValue.Database.MasterRoom));
			var masterRoomContents = (await parser.Mediator.Send(new GetContentsQuery(masterRoom)) ?? []);

			var masterRoomContent =
				await parser.CommandDiscoveryService.MatchUserDefinedCommand(parser,
					masterRoomContents.Select(x => x.WithRoomOption()),
					arg0);

			foreach (var (i, (obj, attr, _)) in masterRoomContent.AsValue().Index())
			{
				runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
				await parser.NotifyService.Notify(executor,
					$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]");
			}
		}

		return new CallState(string.Join(" ", runningOutput));
	}

	[SharpCommand(Name = "@SWITCH",
		Switches = ["NOTIFY", "FIRST", "ALL", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Switch(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WAIT", Switches = ["PID", "UNTIL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Wait(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message!.ToPlainText()!;
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		/*
		 *  @wait/pid <pid>=<seconds>
		 *	@wait/pid <pid>=[+-]<adjustment>
		 *	@wait/pid/until <pid>=<time>
		 */
		if (switches.Contains("PID"))
		{
			return await AtWaitForPid(parser, arg0, executor, arg1?.ToPlainText(), switches);
		}

		if (arg1 is null)
		{
			await parser.NotifyService.Notify(executor, "Command list missing");
			return new CallState("#-1 MISSING COMMAND LIST ARGUMENT");
		}

		//  @wait[/until] <time>=<command_list>
		if (double.TryParse(arg0, out var time))
		{
			TimeSpan convertedTime;
			if (!switches.Contains("UNTIL"))
			{
				convertedTime = DateTimeOffset.FromUnixTimeSeconds((long)time) - DateTimeOffset.UtcNow;
			}
			else
			{
				convertedTime = TimeSpan.FromSeconds(time);
			}

			await parser.Mediator.Publish(new QueueDelayedCommandListRequest(arg1, parser.CurrentState, convertedTime));
			return CallState.Empty;
		}

		var splitBySlashes = arg0.Split('/');

		// @wait <object>=<command_list>
		if (splitBySlashes.Length == 1)
		{
			var maybeObject = await
				parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0, LocateFlags.All);

			if (maybeObject.IsError)
			{
				return maybeObject.AsError;
			}

			var located = maybeObject.AsSharpObject;
			if (!await parser.PermissionService.Controls(executor, located))
			{
				await parser.NotifyService.Notify(executor, "Permission Denied.");
				return new CallState(Errors.ErrorPerm);
			}

			await QueueSemaphore(parser, located, ["SEMAPHORE"], arg1);
			return CallState.Empty;
		}

		var maybeLocate =
			await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, splitBySlashes[0],
				LocateFlags.All);
		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var foundObject = maybeLocate.AsSharpObject;
		var untilTime = 0.0d;

		switch (splitBySlashes.Length)
		{
			// @wait[/until] <object>/<time>=<command_list>
			// @wait <object>/<attribute>=<command_list>
			case 2 when switches.Contains("UNTIL"):
			{
				if (!double.TryParse(splitBySlashes[1], out untilTime))
				{
					await parser.NotifyService.Notify(executor, "Invalid time argument format");
					return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "TIME ARGUMENT"));
				}

				var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;

				await QueueSemaphoreWithDelay(parser, foundObject, ["SEMAPHORE"], newUntilTime, arg1);
				return CallState.Empty;
			}

			case 2 when double.TryParse(splitBySlashes[1], out untilTime):
				await QueueSemaphoreWithDelay(parser, foundObject, ["SEMAPHORE"], TimeSpan.FromSeconds(untilTime), arg1);
				return CallState.Empty;

			// TODO: Ensure the attribute has the same flags as the SEMAPHORE @attribute, otherwise it can't be used!
			case 2:
				await QueueSemaphore(parser, foundObject, splitBySlashes[1].Split('`'), arg1);
				return CallState.Empty;

			// @wait[/until] <object>/<attribute>/<time>=<command list>
			case 3 when !double.TryParse(splitBySlashes[2], out untilTime):
				await parser.NotifyService.Notify(executor, "Invalid time argument format");
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "TIME ARGUMENT"));
			
			// TODO: Validate valid attribute value.
			case 3 when switches.Contains("UNTIL"):
			{
				var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;
				await QueueSemaphoreWithDelay(parser, foundObject, splitBySlashes[1].Split('`'), newUntilTime, arg1);
				return CallState.Empty;
			}

			case 3:
				await QueueSemaphoreWithDelay(parser, foundObject, splitBySlashes[1].Split('`'), TimeSpan.FromSeconds(untilTime), arg1);
				return CallState.Empty;

			default:
				await parser.NotifyService.Notify(executor, "Invalid first argument format");
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "FIRST ARGUMENT"));
		}
	}

	private static async ValueTask QueueSemaphore(IMUSHCodeParser parser, AnySharpObject located, string[] attribute,
		MString arg1)
	{
		var one = await parser.Mediator.Send(new GetObjectNodeQuery(new DBRef(0)));
		var attrValues = await parser.Mediator.Send(new GetAttributeQuery(located.Object().DBRef, ["SEMAPHORE"]));
		var attrValue = attrValues?.LastOrDefault();

		if (attrValue is null)
		{
			await parser.Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single("0"),
				one.AsPlayer));
			await parser.Mediator.Publish(new QueueCommandListRequest(arg1, parser.CurrentState,
				new DbRefAttribute(located.Object().DBRef, attribute), 0));
		}

		var last = int.Parse(attrValue!.Value.ToPlainText());
		await parser.Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single($"{last + 1}"),
			one.AsPlayer));
		await parser.Mediator.Publish(new QueueCommandListRequest(arg1, parser.CurrentState,
			new DbRefAttribute(located.Object().DBRef, attribute), last));
	}

	private static async ValueTask QueueSemaphoreWithDelay(IMUSHCodeParser parser, AnySharpObject located,
		string[] attribute, TimeSpan delay, MString arg1)
	{
		var one = await parser.Mediator.Send(new GetObjectNodeQuery(new DBRef(0)));
		var attrValues = await parser.Mediator.Send(new GetAttributeQuery(located.Object().DBRef, attribute));
		var attrValue = attrValues?.LastOrDefault();

		if (attrValue is null)
		{
			await parser.Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single("0"),
				one.AsPlayer));
			await parser.Mediator.Publish(new QueueCommandListWithTimeoutRequest(arg1, parser.CurrentState,
				new DbRefAttribute(located.Object().DBRef, attribute), 0, delay));
		}

		var last = int.Parse(attrValue!.Value.ToPlainText());
		await parser.Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single($"{last + 1}"),
			one.AsPlayer));
		await parser.Mediator.Publish(new QueueCommandListWithTimeoutRequest(arg1, parser.CurrentState,
			new DbRefAttribute(located.Object().DBRef, attribute), last, delay));
	}

	private static async ValueTask<Option<CallState>> AtWaitForPid(IMUSHCodeParser parser, string? arg0,
		AnySharpObject executor, string? arg1,
		string[] switches)
	{
		if (!int.TryParse(arg0, out var pid))
		{
			await parser.NotifyService.Notify(executor, "Invalid PID specified.");
			return new CallState("#-1 INVALID PID");
		}

		if (string.IsNullOrEmpty(arg1))
		{
			await parser.NotifyService.Notify(executor, "What do you want to do with the process?");
			return new CallState(string.Format(Errors.ErrorTooFewArguments, "@WAIT", 2, 1));
		}

		var exists = await parser.Mediator.Send(new ScheduleSemaphoreQuery(pid));
		var maybeFoundPid = await exists.FirstOrDefaultAsync();

		if (maybeFoundPid is null)
		{
			await parser.NotifyService.Notify(executor, "Invalid PID specified.");
			return new CallState("#-1 INVALID PID");
		}

		var timeArg = arg1;

		if (switches.Contains("UNTIL"))
		{
			if (!DateTimeOffset.TryParse(timeArg, out var dateTimeOffset))
			{
				await parser.NotifyService.Notify(executor, "Invalid time specified.");
				return new CallState("#-1 INVALID TIME");
			}

			var until = DateTimeOffset.UtcNow - dateTimeOffset;
			await parser.Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));

			return new CallState(maybeFoundPid.Pid.ToString());
		}

		if (arg1.StartsWith('+') || arg1.StartsWith('-'))
		{
			timeArg = arg1.Skip(1).ToString();
		}

		if (!long.TryParse(timeArg, out var secs))
		{
			await parser.NotifyService.Notify(executor, "Invalid time specified.");
			return new CallState("#-1 INVALID TIME");
		}

		if (arg1.StartsWith('+'))
		{
			var until = (maybeFoundPid.RunDelay ?? TimeSpan.Zero) + TimeSpan.FromSeconds(secs);
			await parser.Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));
			return new CallState(maybeFoundPid.Pid.ToString());
		}

		if (arg1.StartsWith('-'))
		{
			var until = (maybeFoundPid.RunDelay ?? TimeSpan.Zero) - TimeSpan.FromSeconds(secs);
			await parser.Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));
			return new CallState(maybeFoundPid.Pid.ToString());
		}

		await parser.Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, TimeSpan.FromSeconds(secs)));
		return new CallState(maybeFoundPid.Pid.ToString());
	}

	[SharpCommand(Name = "@COMMAND",
		Switches =
		[
			"ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE",
			"DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"
		], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Command(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DRAIN", Switches = ["ALL", "ANY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 1,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Drain(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message?.ToPlainText();
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var one = await parser.Mediator.Send(new GetObjectNodeQuery(new DBRef(0)));

		if (switches.Length > 1)
		{
			await parser.NotifyService.Notify(executor, Errors.ErrorTooManySwitches);
			return new CallState(Errors.ErrorTooManySwitches);
		}

		var maybeObjectAndAttribute = HelperFunctions.SplitDBRefAndOptionalAttr(arg0);
		if (maybeObjectAndAttribute is { IsT1: true, AsT1: false })
		{
			await parser.NotifyService.Notify(executor, Errors.ErrorCantSeeThat);
			return new CallState(Errors.ErrorCantSeeThat);
		}

		var (target, maybeAttribute) = maybeObjectAndAttribute.AsT0;
		var maybeObject = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target,
			LocateFlags.All);

		switch (maybeObject)
		{
			case { IsError: true }:
				return new CallState(maybeObject.AsError.Value);
			case { IsNone: true }:
				return new CallState(Errors.ErrorCantSeeThat);
		}

		var objectToDrain = maybeObject.AsAnyObject;
		var attribute = maybeAttribute?.Split("`") ?? ["SEMAPHORE"];

		//   @drain[/any][/all] <object>[/<attribute>][=<number>]
		if (maybeAttribute is not null && (switches.Contains("ANY") || switches.Length == 0))
		{
			var maybeFoundAttributes =
				await parser.Mediator.Send(new GetAttributeQuery(objectToDrain.Object().DBRef, attribute));
			var maybeFoundAttribute = maybeFoundAttributes?.LastOrDefault();

			if (maybeFoundAttribute is null)
			{
				await parser.Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, attribute,
					MModule.single("-1"),
					one.AsPlayer));
			}

			await parser.Mediator.Publish(
				new DrainSemaphoreRequest(new DbRefAttribute(objectToDrain.Object().DBRef, attribute)));
		}
		else if (maybeAttribute is null && (switches.Contains("ANY") || switches.Length == 0))
		{
			var pids = await parser.Mediator.Send(new ScheduleSemaphoreQuery(objectToDrain.Object().DBRef));
			await foreach (var uniqueAttribute in pids
				               .GroupBy(data => string.Join('`', data.SemaphoreSource.Attribute), x => x.SemaphoreSource)
				               .Select(x => x.FirstAsync()))
			{
				var dbRefAttrToDrain = await uniqueAttribute;
				await parser.Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, dbRefAttrToDrain.Attribute,
					MModule.single("-1"),
					one.AsPlayer));
				await parser.Mediator.Publish(new DrainSemaphoreRequest(dbRefAttrToDrain));
			}
		}
		else if (switches.Contains("ALL"))
		{
			// TODO: Figure out the wording of the helpfile, or go rummaging in the source docs, because something feels funky.
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@FORCE", Switches = ["NOEVAL", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSBrace, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Force(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var objArg = Functions.Functions.NoParseDefaultNoParseArgument(args, 0, MModule.empty());
		var cmdListArg = Functions.Functions.NoParseDefaultNoParseArgument(args, 1, MModule.empty());
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		
		var maybeFound =
			await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objArg.ToPlainText(), LocateFlags.All);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		var found = maybeFound.AsSharpObject;

		if (!await parser.PermissionService.Controls(executor, found))
		{
			await parser.NotifyService.Notify(executor, "Permission denied. You do not control the target.");
			return new CallState(Errors.ErrorPerm);
		}
		
		if (cmdListArg.Length < 1)
		{
			await parser.NotifyService.Notify(executor, "Force them to do what?");
			// TODO: Introduce Error
			return new CallState("#-1 NOTHING TO DO");
		}
		
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@IFELSE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> IfElse(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var truthy = Predicates.Truthy(parsedIfElse!);

		if (truthy)
		{
			await parser.CommandListParse(parser.CurrentState.Arguments["1"].Message!);
		}
		else if (parser.CurrentState.Arguments.TryGetValue("2", out var arg2))
		{
			await parser.CommandListParse(arg2.Message!);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NSEMIT", Switches = ["ROOM", "NOEVAL", "SILENT"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSREMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PROMPT", Switches = ["SILENT", "NOISY", "NOEVAL", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Prompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SEARCH", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Search(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WHEREIS", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WhereIs(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@BREAK", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Break(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Inline does nothing.
		var args = parser.CurrentState.Arguments;
		var nargs = args.Count;
		switch (nargs)
		{
			case 0:
				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				break;
			case 1:
				if (args["0"].Message.Truthy())
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}
				return args["0"];
			case 2:
				if (args["0"].Message.Truthy())
				{
					var command = await args["1"].ParsedMessage();
					var commandList = parser.CommandListParseVisitor(command!);
					await commandList();
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}
				return args["0"];
		}
		return CallState.Empty;
	}

	[SharpCommand(Name = "@CONFIG", Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Config(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@EDIT", Switches = ["FIRST", "CHECK", "QUIET", "REGEXP", "NOCASE", "ALL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Edit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FUNCTION",
		Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Function(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSLEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PS", Switches = ["ALL", "SUMMARY", "COUNT", "QUICK", "DEBUG"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ProcessStatus(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SELECT",
		Switches = ["NOTIFY", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Select(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@TRIGGER",
		Switches = ["CLEARREGS", "SPOOF", "INLINE", "NOBREAK", "LOCALIZE", "INPLACE", "MATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Trigger(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
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
	public static async ValueTask<Option<CallState>> Channel(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		/*
		    @channel/list[/on|/off][/quiet] [<prefix>]
			  @channel/what [<prefix>]
			  @channel/who <channel>
			  @channel/on <channel>[=<player>]
			  @channel/off <channel>[=<player>]
			  @channel/gag [<channel>][=<yes|no>]
				@channel/mute [<channel>][=<yes|no>]
				@channel/hide [<channel>][=<yes|no>]
				@channel/combine [<channel>][=<yes|no>]
			  @channel/title <channel>[=<message>]
			  @channel/recall[/quiet] <channel>[=<lines|duration>[, <start line>]]  @channel/add <channel>[=<privs>]
			  @channel/privs <channel>=<privs>
			  @channel/describe <channel>=<description>
			  @channel/buffer <channel>=<lines>
			  @channel/decompile[/brief] <prefix>
			  @channel/chown <channel>=<new owner>
				@channel/rename <channel>=<new name>
				@channel/wipe <channel>
				@channel/delete <channel>
				@channel/mogrifier <channel>=<object>
		 */
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("QUIET") && (!switches.Contains("LIST") || !switches.Contains("RECALL")))
		{
			return new CallState("CHAT: INCORRECT COMBINATION OF SWITCHES");
		}

		// TODO: Channel Visibility on most of these commands.

		return switches switch
		{
			[.., "LIST"] => await ChannelCommand.ChannelList.Handle(parser, args["0"].Message!, args["1"].Message!, switches),
			["WHAT"] => await ChannelWhat.Handle(parser, args["0"].Message!),
			["WHO"] => await ChannelWho.Handle(parser, args["0"].Message!),
			["ON"] or ["JOIN"] => await ChannelOn.Handle(parser, args["0"].Message!, args["1"].Message),
			["OFF"] or ["LEAVE"] => await ChannelOff.Handle(parser, args["0"].Message!, args["1"].Message),
			["GAG"] => await ChannelGag.Handle(parser, args["0"].Message!, args["1"].Message!, switches),
			["MUTE"] => await ChannelMute.Handle(parser, args["0"].Message!, args["1"].Message!),
			["HIDE"] => await ChannelHide.Handle(parser, args["0"].Message!, args["1"].Message!),
			["COMBINE"] => await ChannelCombine.Handle(parser, args["0"].Message!, args["1"].Message!),
			["TITLE"] => await ChannelTitle.Handle(parser, args["0"].Message!, args["1"].Message!),
			[.., "RECALL"] => await ChannelRecall.Handle(parser, args["0"].Message!, args["1"].Message!, switches),
			["ADD"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelAdd.Handle(parser, args["0"].Message!, args["1"].Message!),
			["PRIVS"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelPrivs.Handle(parser, args["0"].Message!, args["1"].Message!),
			["DESCRIBE"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelDescribe.Handle(parser, args["0"].Message!, args["1"].Message!),
			["BUFFER"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelBuffer.Handle(parser, args["0"].Message!, args["1"].Message!),
			[.., "DECOMPILE"] => await ChannelDecompile.Handle(parser, args["0"].Message!, args["1"].Message!, switches),
			["CHOWN"] => await ChannelChown.Handle(parser, args["0"].Message!, args["1"].Message!),
			["RENAME"] => await ChannelRename.Handle(parser, args["0"].Message!, args["1"].Message!),
			["WIPE"] => await ChannelWipe.Handle(parser, args["0"].Message!, args["1"].Message!),
			["DELETE"] => await ChannelDelete.Handle(parser, args["0"].Message!, args["1"].Message!),
			["MOGRIFIER"] => await ChannelMogrifier.Handle(parser, args["0"].Message!, args["1"].Message!),
			_ => new CallState("What do you want to do with the channel?")
		};
	}

	[SharpCommand(Name = "@DECOMPILE", Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Decompile(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.RSNoParse | CB.NoGagged,
		MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Emit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(parser.Mediator);
		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(parser);
		var isSpoof = parser.CurrentState.Switches.Contains("SPOOF");
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? Functions.Functions.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await Functions.Functions.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.ToAsyncEnumerable()
			.WhereAwait(async obj =>
				await parser.PermissionService.CanInteract(obj.WithRoomOption(), executor,
					IPermissionService.InteractType.Hear));

		if (isSpoof)
		{
			var canSpoof = await executor.HasPower("CAN_SPOOF");
			var controlsExecutor = await parser.PermissionService.Controls(executor, enactor);

			if (!canSpoof && !controlsExecutor)
			{
				await parser.NotifyService.Notify(executor, "You do not have permission to spoof emits.");
				return new CallState(Errors.ErrorPerm);
			}
		}

		await foreach (var obj in interactableContents)
		{
			await parser.NotifyService.Notify(
				obj.WithRoomOption(),
				message,
				isSpoof ? enactor : executor,
				INotifyService.NotificationType.Emit);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "@LISTMOTD", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ListMessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSOEMIT", Switches = ["NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSNoParse, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(parser.Mediator);
		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(parser);
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? Functions.Functions.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await Functions.Functions.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.ToAsyncEnumerable()
			.WhereAwait(async obj =>
				await parser.PermissionService.CanInteract(obj.WithRoomOption(), executor,
					IPermissionService.InteractType.Hear));

		var canSpoof = await executor.HasPower("CAN_SPOOF");
		var controlsExecutor = await parser.PermissionService.Controls(executor, enactor);

		if (!canSpoof && !controlsExecutor)
		{
			await parser.NotifyService.Notify(executor, "You do not have permission to spoof emits.");
			return new CallState(Errors.ErrorPerm);
		}

		await foreach (var obj in interactableContents)
		{
			await parser.NotifyService.Notify(
				obj.WithRoomOption(),
				message,
				enactor,
				INotifyService.NotificationType.Emit);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "@OEMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> OmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@REMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> RoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@STATS", Switches = ["CHUNKS", "FREESPACE", "PAGING", "REGIONS", "TABLES", "FLAGS"],
		Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Stats(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@VERB", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Verb(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CHAT", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Chat(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		if (!arg0Check || !arg1Check)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var channelName = arg0CallState!.Message!;
		var message = arg1CallState!.Message!;

		// TODO: Use standardized method.
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

		if (maybeMemberStatus is null)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "You are not a member of that channel.");
			return new CallState("#-1 You are not a member of that channel.");
		}

		var (_, status) = maybeMemberStatus.Value;

		await parser.Mediator.Send(new ChannelMessageNotification(
			channel,
			executor.WithNoneOption(),
			INotifyService.NotificationType.Emit,
			message,
			status.Title ?? MModule.empty(),
			MModule.single(executor.Object().Name),
			MModule.single("says"),
			[]
		));

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@ENTRANCES", Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Entrances(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@GREP", Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Grep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@INCLUDE", Switches = ["LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Include(IMUSHCodeParser parser, SharpCommandAttribute _2)
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
		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		MString? arg0, arg1;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var caller = (await parser.CurrentState.CallerObject(parser.Mediator)).Known();
		string[] sendSwitches = ["SEND", "URGENT", "NOSIG", "SILENT", "NOEVAL"];

		if (switches.Except(sendSwitches).Any() && switches.Length > 1)
		{
			await parser.NotifyService.Notify(executor, "Error: Too many switches passed to @mail.", caller);
			return new CallState(Errors.ErrorTooManySwitches);
		}

		if (!switches.Contains("NOEVAL"))
		{
			arg0 = await (arg0CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
			arg1 = await (arg1CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
		}
		else
		{
			arg0 = arg0CallState?.Message;
			arg1 = arg1CallState?.Message;
		}

		var response = switches.AsSpan() switch
		{
			[.., "FOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, arg0, arg1, switches),
			[.., "UNFOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, arg0, arg1, switches),
			[.., "FILE"] when executor.IsPlayer => await FolderMail.Handle(parser, arg0, arg1, switches),
			[.., "CLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, arg0, arg1, "CLEAR"),
			[.., "UNCLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, arg0, arg1, "UNCLEAR"),
			[.., "TAG"] when executor.IsPlayer => await StatusMail.Handle(parser, arg0, arg1, "TAG"),
			[.., "UNTAG"] when executor.IsPlayer => await StatusMail.Handle(parser, arg0, arg1, "UNTAG"),
			[.., "UNREAD"] when executor.IsPlayer => await StatusMail.Handle(parser, arg0, arg1, "UNREAD"),
			[.., "STATUS"] when executor.IsPlayer => await StatusMail.Handle(parser, arg0, arg1, "STATUS"),
			[.., "CSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, arg0, switches),
			[.., "STATS"] when executor.IsPlayer => await StatsMail.Handle(parser, arg0, switches),
			[.., "DSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, arg0, switches),
			[.., "FSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, arg0, switches),
			[.., "DEBUG"] => await AdminMail.Handle(parser, switches),
			[.., "NUKE"] => await AdminMail.Handle(parser, switches),
			[.., "REVIEW"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await ReviewMail.Handle(parser, arg0, arg1, switches),
			[.., "RETRACT"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await RetractMail.Handle(parser, arg0!.ToPlainText(), arg1!.ToPlainText()),
			[.., "FWD"] when executor.IsPlayer && int.TryParse(arg0?.ToPlainText(), out var number) &&
			                 (arg1?.Length ?? 0) != 0
				=> await ForwardMail.Handle(parser, number, arg1!.ToPlainText()),
			[.., "SEND"] or [.., "URGENT"] or [.., "SILENT"] or [.., "NOSIG"] or []
				when arg0?.Length != 0 && arg1?.Length != 0
				=> await SendMail.Handle(parser, arg0!, arg1!, switches),
			[.., "READ"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0 &&
			                        int.TryParse(arg0?.ToPlainText(), out var number)
				=> await ReadMail.Handle(parser, Math.Max(0, number - 1), switches),
			[.., "LIST"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0
				=> await ListMail.Handle(parser, arg0, arg1, switches),
			_ => MModule.single("#-1 BAD ARGUMENTS TO MAIL COMMAND")
		};

		return new CallState(response);
	}

	[SharpCommand(Name = "@NSPEMIT", Switches = ["LIST", "SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PASSWORD", Switches = [],
		Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Password(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var oldPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var newPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (!executor.IsPlayer)
		{
			await parser.NotifyService.Notify(executor, "Only players have passwords.");
			return new CallState("#-1 INVALID OBJECT TYPE.");
		}

		var isValidPassword = parser.PasswordService.PasswordIsValid(executor.Object().DBRef.ToString(), oldPassword,
			executor.AsPlayer.PasswordHash);
		if (!isValidPassword)
		{
			await parser.NotifyService.Notify(executor, "Invalid password.");
			return new CallState("#-1 INVALID PASSWORD.");
		}

		var hashedPassword = parser.PasswordService.HashPassword(executor.Object().DBRef.ToString(), newPassword);
		await parser.PasswordService.SetPassword(executor.AsPlayer, hashedPassword);

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Restart(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SWEEP", Switches = ["CONNECTED", "HERE", "INVENTORY", "EXITS"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Sweep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@VERSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Version(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		// TODO: Last Restarted
		var result = MModule.multipleWithDelimiter(
			MModule.single(Environment.NewLine),
			[
				MModule.concat(MModule.single("You are connected to "),
					MModule.single(parser.Configuration.CurrentValue.Net.MudName)),
				MModule.concat(MModule.single("Address: "), MModule.single(parser.Configuration.CurrentValue.Net.MudUrl)),
				MModule.single("SharpMUSH version 0")
			]);

		await parser.NotifyService.Notify(executor, result);

		return new CallState(result);
	}

	[SharpCommand(Name = "@RETRY", Switches = [],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Retry(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ASSERT", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Assert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Inline does nothing.
		var args = parser.CurrentState.Arguments;
		var nargs = args.Count;
		switch (nargs)
		{
			case 0:
				// Do nothing.
				break;
			case 1:
				if (args["0"].Message.Falsy())
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}
				return args["0"];
			case 2:
				if (args["0"].Message.Falsy())
				{
					var command = await args["1"].ParsedMessage();
					var commandList = parser.CommandListParseVisitor(command!);
					await commandList();
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}
				return args["0"];
		}
		return CallState.Empty;
	}

	[SharpCommand(Name = "@ATTRIBUTE",
		Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Attribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SKIP", Switches = ["IFELSE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Skip(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MESSAGE", Switches = ["NOEVAL", "SPOOF", "NOSPOOF", "REMIT", "OEMIT", "SILENT", "NOISY"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Message(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NSCEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofChannelEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}