﻿using ANSILibrary;
using OneOf.Monads;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using System.Drawing;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

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

		var notification = args[0]!.Message!.ToString();
		var enactor = parser.CurrentState.Enactor!.Value;
		await parser.NotifyService.Notify(enactor, notification);

		return new None();
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = parser.CurrentState.Enactor!.Value;
		await parser.NotifyService.Notify(enactor, "Huh?  (Type \"help\" for help.)");
		return new None();
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> GoTo(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = parser.CurrentState.Enactor!.Value;
		
		if(parser.CurrentState.Arguments.Count < 1)
		{
			await parser.NotifyService.Notify(enactor, "You can't go that way.");
			return new None();
		}

		// TODO: Implement
		// Find the target, make sure it's a valid exit.
		// Figure out the Destination if there's a Destination Attribute.
		// Otherwise, check the Home of the Exit.
		// Move the player to the new location.

		return new None();
	}

	[SharpCommand(Name = "LOOK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Consult CONFORMAT, DESCFORMAT, INAMEFORMAT, NAMEFORMAT, etc.
		
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();
		AnyOptionalSharpObject viewing = new OneOf.Types.None();

		if (args.Count == 1)
		{
			var locate = await parser.LocateService.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				args[0]!.Message!.ToString(),
				Library.Services.LocateFlags.All);
			
			if(locate.IsValid())
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
		
		var contents = (await parser.Database.GetContentsAsync(viewing))!;
		var viewingObject = viewing.Object()!;

		var name = viewingObject.Name;
		var location = viewingObject.Key;
		var contentKeys = contents.Select(x => x.Object()!.Name);
		var exitKeys = (await parser.Database.GetExitsAsync(viewingObject.DBRef))?.FirstOrDefault();
		var description = (await parser.AttributeService.GetAttributeAsync(enactor, viewing.Known(), "DESCRIBE", Library.Services.IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => string.IsNullOrEmpty(attr.Value) 
					? "There is nothing to see here" 
					: attr.Value, 
				none => "There is nothing to see here",
				error => string.Empty);

		// TODO: Pass value into NAMEFORMAT
		await parser.NotifyService.Notify(enactor, $"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(name))}" +
			$"(#{viewingObject.DBRef.Number}{string.Join(string.Empty,viewingObject.Flags().Select(x => x.Symbol))})" );
		// TODO: Pass value into DESCFORMAT
		await parser.NotifyService.Notify(enactor, description);
		// parser.NotifyService.Notify(enactor, $"Location: {location}");
		// TODO: Pass value into CONFORMAT
		await parser.NotifyService.Notify(enactor, $"Contents: {string.Join(Environment.NewLine, contentKeys)}");
		// TODO: Pass value into EXITFORMAT
		await parser.NotifyService.Notify(enactor, $"Exits: {string.Join(Environment.NewLine, exitKeys)}");

		return new CallState(viewingObject.DBRef.ToString());
	}

	[SharpCommand(Name = "EXAMINE", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();
		AnyOptionalSharpObject viewing = new OneOf.Types.None();

		if (args.Count == 1)
		{
			var locate = await parser.LocateService.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				args[0]!.Message!.ToString(),
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
		var description = (await parser.AttributeService.GetAttributeAsync(enactor, viewing.Known(), "DESCRIBE", Library.Services.IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => string.IsNullOrEmpty(attr.Value)
					? "There is nothing to see here"
					: attr.Value,
				none => "There is nothing to see here",
				error => string.Empty);

		await parser.NotifyService.Notify(enactor, $"{name.Hilight()}" +
		                                     $"(#{obj.DBRef.Number}{string.Join(string.Empty, obj.Flags().Select(x => x.Symbol))})");
		await parser.NotifyService.Notify(enactor, $"Type: {obj.Type} Flags: {string.Join(" ",obj.Flags().Select(x => x.Name))}");
		await parser.NotifyService.Notify(enactor, description);
		await parser.NotifyService.Notify(enactor, $"Owner: {ownerName.Hilight()}" +
		                                           $"(#{obj.DBRef.Number}{string.Join(string.Empty, ownerObj.Flags().Select(x => x.Symbol))})");
		// TODO: Zone & Money
		await parser.NotifyService.Notify(enactor, $"Parent: {obj.Parent()?.Name ?? "*NOTHING*"}");
		// TODO: LOCK LIST
		await parser.NotifyService.Notify(enactor, $"Powers: {string.Join(" ",obj.Powers().Select(x => x.Name))}");
		// TODO: Channels
		// TODO: Warnings Checked

		// TODO: Match proper date format: Mon Feb 26 18:05:10 2007
		await parser.NotifyService.Notify(enactor, $"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime).ToString("F")}");

		var atrs = await parser.AttributeService.GetVisibleAttributesAsync(enactor, viewing.Known());
		
		if (atrs.IsAttribute)
		{
			foreach (var attr in atrs.AsAttributes)
			{
				// TODO: Symbols for Flags. Flags are not just strings!
				await parser.NotifyService.Notify(enactor, 
					$"{attr.Name} [#{attr.Owner().Object.DBRef.Number}]: ".Hilight()
					+ attr.Value);
			}
		}

		// TODO: Proper carry format.
		await parser.NotifyService.Notify(enactor, $"Contents: {Environment.NewLine}" +
		                                           $"{string.Join(Environment.NewLine, contentKeys)}");

		if(!viewing.IsRoom)
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

		var notification = args[1]!.Message!.ToString();
		var targetListText = MModule.plainText(args[0]!.Message!);
		var nameListTargets = Functions.Functions.NameList(targetListText);
		
		var enactor = parser.Database.GetObjectNode(parser.CurrentState.Executor!.Value).Known();

		foreach(var target in nameListTargets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var locateTarget = await parser.LocateService.LocateAndNotifyIfInvalid(parser, enactor, enactor, targetString, Library.Services.LocateFlags.All);

			if(locateTarget.IsValid())
			{
				await parser.NotifyService.Notify(locateTarget.WithoutError().Known().Object().DBRef, notification);
			}
		}

		return new None();
	}
}
