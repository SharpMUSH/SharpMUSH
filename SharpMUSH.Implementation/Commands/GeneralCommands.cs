using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Drawing;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (args.Count < 1)
		{
			return new None();
		}

		var notification = args["0"].Message!.ToString();
		var enactor = parser.CurrentState.Enactor!.Value;
		await parser.NotifyService.Notify(enactor, notification);

		return new None();
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// </remarks>
	[SharpCommand(Name = "@PCREATE", Behavior = CB.Default, MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> PCreate(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Validate Name and Passwords
		var args = parser.CurrentState.Arguments;
		var name = MModule.plainText(args["0"].Message!);
		var password = MModule.plainText(args["1"].Message!);

		var player = await parser.Database.CreatePlayerAsync(name, password, parser.CurrentState.Executor!.Value);

		return new CallState(player.ToString());
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
		var executor = parser.CurrentState.ExecutorObject(parser.Database).Known();

		var thing = await parser.Database.CreateThingAsync(name, executor.Where, executor.Object()!.Owner());

		return new CallState(thing.ToString());
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = parser.CurrentState.Enactor!.Value;
		await parser.NotifyService.Notify(enactor, "Huh?  (Type \"help\" for help.)");
		return new None();
	}

	[SharpCommand(Name = "@DOLIST", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY"])]
	public static async ValueTask<Option<CallState>> DoList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = parser.CurrentState.Enactor!.Value;

		if (parser.CurrentState.Arguments.Count < 2)
		{
			await parser.NotifyService.Notify(enactor, "What do you want to do with the list?");
			return new None();
		}

		var list = MModule.split(" ", parser.CurrentState.Arguments["0"].Message!);

		var wrappedIteration = new IterationWrapper<MString> { Value = MModule.empty() };
		parser.CurrentState.IterationRegisters.Push(wrappedIteration);
		var command = parser.CurrentState.Arguments["1"].Message!;

		foreach (var item in list)
		{
			wrappedIteration.Value = item!;
			wrappedIteration.Iteration++;
			await parser.CommandListParse(command);
		}

		parser.CurrentState.IterationRegisters.Pop();

		return new None();
	}

	[SharpCommand(Name = "LOOK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Consult CONFORMAT, DESCFORMAT, INAMEFORMAT, NAMEFORMAT, etc.

		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();
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
			viewing = (await parser.Database.GetLocationAsync(enactor.Object().DBRef, 1)).WithExitOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var contents = (await parser.Database.GetContentsAsync(viewing))!.ToList();
		var viewingObject = viewing.Object()!;

		var name = viewingObject.Name;
		var location = viewingObject.Key;
		var contentKeys = contents.Select(x => x.Object()!.Name).ToList();
		var exitKeys = await parser.Database.GetExitsAsync(viewingObject.DBRef) ?? [];
		var description = (await parser.AttributeService.GetAttributeAsync(enactor, viewing.Known(), "DESCRIBE",
				Library.Services.IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Value,
				none => MModule.single("There is nothing to see here"),
				error => MModule.empty());

		// TODO: Pass value into NAMEFORMAT
		await parser.NotifyService.Notify(enactor,
			$"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(name))}" +
			$"(#{viewingObject.DBRef.Number}{string.Join(string.Empty, viewingObject.Flags().Select(x => x.Symbol))})");
		// TODO: Pass value into DESCFORMAT
		await parser.NotifyService.Notify(enactor, description.ToString());
		// parser.NotifyService.Notify(enactor, $"Location: {location}");
		// TODO: Pass value into CONFORMAT
		await parser.NotifyService.Notify(enactor, $"Contents: {string.Join(Environment.NewLine, contentKeys)}");
		// TODO: Pass value into EXITFORMAT
		await parser.NotifyService.Notify(enactor,
			$"Exits: {string.Join(Environment.NewLine, string.Join(", ", exitKeys.Select(x => x.Object)))}");

		return new CallState(viewingObject.DBRef.ToString());
	}

	[SharpCommand(Name = "EXAMINE", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();
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
			viewing = (await parser.Database.GetLocationAsync(enactor.Object().DBRef, 1)).WithExitOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var contents = await parser.Database.GetContentsAsync(viewing);

		var obj = viewing.Object()!;
		var ownerObj = obj.Owner()!.Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var location = obj.Key;
		var contentKeys = contents!.Select(x => x.Object()!.Name);
		var exitKeys = (await parser.Database.GetExitsAsync(obj.DBRef))?.FirstOrDefault();
		var description = (await parser.AttributeService.GetAttributeAsync(enactor, viewing.Known(), "DESCRIBE",
				Library.Services.IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Value,
				none => MModule.single("There is nothing to see here"),
				error => MModule.empty());

		await parser.NotifyService.Notify(enactor, $"{name.Hilight()}" +
																							 $"(#{obj.DBRef.Number}{string.Join(string.Empty, obj.Flags().Select(x => x.Symbol))})");
		await parser.NotifyService.Notify(enactor,
			$"Type: {obj.Type} Flags: {string.Join(" ", obj.Flags().Select(x => x.Name))}");
		await parser.NotifyService.Notify(enactor, description.ToString());
		await parser.NotifyService.Notify(enactor, $"Owner: {ownerName.Hilight()}" +
																							 $"(#{obj.DBRef.Number}{string.Join(string.Empty, ownerObj.Flags().Select(x => x.Symbol))})");
		// TODO: Zone & Money
		await parser.NotifyService.Notify(enactor, $"Parent: {obj.Parent()?.Name ?? "*NOTHING*"}");
		// TODO: LOCK LIST
		await parser.NotifyService.Notify(enactor, $"Powers: {string.Join(" ", obj.Powers().Select(x => x.Name))}");
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
						$"{attr.Name} [#{attr.Owner().Object.DBRef.Number}]: "
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

		var notification = args["1"]!.Message!.ToString();
		var targetListText = MModule.plainText(args["0"]!.Message!);
		var nameListTargets = Functions.Functions.NameList(targetListText);

		var enactor = parser.Database.GetObjectNode(parser.CurrentState.Executor!.Value).Known();

		foreach (var target in nameListTargets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var locateTarget = await parser.LocateService.LocateAndNotifyIfInvalid(parser, enactor, enactor, targetString,
				Library.Services.LocateFlags.All);

			if (locateTarget.IsValid())
			{
				await parser.NotifyService.Notify(locateTarget.WithoutError().Known().Object().DBRef, notification);
			}
		}

		return new None();
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Goto(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value;
		var enactorObj = parser.CurrentState.EnactorObject(parser.Database).Known();
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
			Library.Services.LocateFlags.ExitsInTheRoomOfLooker);

		if (!exit.IsValid())
		{
			await parser.NotifyService.Notify(enactor, "You can't go that way.");
			return CallState.Empty;
		}

		var exitObj = exit.WithoutError().WithoutNone().AsExit;
		// TODO: Check if the exit has a destination attribute.
		var destinationObj = exitObj.Home();
		var destination = exitObj.Home().Object().DBRef;

		if (!parser.PermissionService.CanGoto(enactorObj, exitObj, exitObj.Home()))
		{
			await parser.NotifyService.Notify(enactor, "You can't go that way.");
			return CallState.Empty;
		}

		await parser.Database.MoveObject(enactorObj.AsContent, destination);

		return new CallState(destination.ToString());
	}


	[SharpCommand(Name = "@TELEPORT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, Switches = ["LIST", "INSIDE", "QUIET"])]
	public static async ValueTask<Option<CallState>> Teleport(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value;

		// If /list - Arg0 can contain multiple SharpObject
		// If not, Arg0 is a singular object
		// Arg0 must only contain SharpContents (Validation)
		// If Arg1 does not exist, Arg0 is the Destination for the Enactor.
		// Otherwise, Arg1 is the Destination for the Arg0.

		var enactorObj = parser.CurrentState.EnactorObject(parser.Database).Known();
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
			toTeleportList = [ isDBRef ? objToTeleport!.Value : toTeleport];
		}

		var toTeleportStringList = toTeleportList.Select(x => x.ToString());

		var destination = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
																																				enactorObj,
																																				enactorObj,
																																				destinationString,
																																				Library.Services.LocateFlags.All);

		if(!destination.IsValid())
		{
			await parser.NotifyService.Notify(enactor, "You can't go that way.");
			return CallState.Empty;
		}

		var validDestination = destination.WithoutError().WithoutNone();

		if(validDestination.IsExit)
		{
			// TODO: Implement Teleporting through an Exit.
			return CallState.Empty;
		}

		var destinationContainer = validDestination.AsContainer;

		foreach (var obj in toTeleportStringList)
		{
			var locateTarget = await parser.LocateService.LocateAndNotifyIfInvalid(parser, enactorObj, enactorObj, obj, Library.Services.LocateFlags.All);
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

			await parser.Database.MoveObject(targetContent, destinationContainer.Object().DBRef);
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
}