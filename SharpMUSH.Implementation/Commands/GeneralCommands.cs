using ANSILibrary;
using MarkupString;
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
	public static Option<CallState> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (args.Count < 1)
		{
			return new None();
		}

		var notification = args[0]!.Message!.ToString();
		var enactor = parser.CurrentState.Enactor!.Value;
		parser.NotifyService.Notify(enactor, notification);

		return new None();
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static Option<CallState> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = parser.CurrentState.Enactor!.Value;
		parser.NotifyService.Notify(enactor, "Huh?  (Type \"help\" for help.)");
		return new None();
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static Option<CallState> GoTo(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = parser.CurrentState.Enactor!.Value;
		
		if(parser.CurrentState.Arguments.Count < 1)
		{
			parser.NotifyService.Notify(enactor, "You can't go that way.");
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
	public static Option<CallState> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Consult CONFORMAT, DESCFORMAT, INAMEFORMAT, NAMEFORMAT, etc.
		
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();
		AnyOptionalSharpObject viewing = new OneOf.Types.None();

		if (args.Count == 1)
		{
			var locate = parser.LocateService.Locate(parser, enactor, enactor, args[0]!.Message!.ToString(), Library.Services.LocateFlags.All);
			
			if(locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}
		else
		{
			viewing = parser.Database.GetLocationAsync(enactor.Object().DBRef, 1).Result.WithExitOption();
		}

		if (viewing.IsNone())
		{
			parser.NotifyService.Notify(enactor, "I can't see that here.");
			return new None();
		}
		
		var contents = parser.Database.GetContentsAsync(viewing).Result;

		var name = viewing.Object()!.Name;
		var location = viewing.Object()!.Key;
		var contentKeys = contents!.Select(x => x.Object()!.Name);
		var exitKeys = parser.Database.GetExitsAsync(viewing.Object()!.DBRef).Result?.FirstOrDefault();
		var description = parser.Database.GetAttributeAsync(viewing.Object()!.DBRef, "DESCRIBE").Result?.FirstOrDefault();

		// TODO: Pass value into NAMEFORMAT
		parser.NotifyService.Notify(enactor, $"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(name))}" +
			$"(#{viewing.Object()!.DBRef.Number}{string.Join(string.Empty,viewing.Object()!.Flags().Select(x => x.Symbol))})" );
		// TODO: Pass value into DESCFORMAT
		parser.NotifyService.Notify(enactor, $"{description?.Value ?? "There is nothing to see here."}");
		// parser.NotifyService.Notify(enactor, $"Location: {location}");
		// TODO: Pass value into CONFORMAT
		parser.NotifyService.Notify(enactor, $"Contents: {string.Join(Environment.NewLine, contentKeys)}");
		// TODO: Pass value into EXITFORMAT
		parser.NotifyService.Notify(enactor, $"Exits: {string.Join(Environment.NewLine, exitKeys)}");

		return new CallState(viewing.Object()!.DBRef.ToString());
	}

	[SharpCommand(Name = "EXAMINE", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static Option<CallState> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();
		AnyOptionalSharpObject viewing = new OneOf.Types.None();

		if (args.Count == 1)
		{
			var locate = parser.LocateService.Locate(parser, enactor, enactor, args[0]!.Message!.ToString(), Library.Services.LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}
		else
		{
			viewing = parser.Database.GetLocationAsync(enactor.Object().DBRef, 1).Result.WithExitOption();
		}

		if (viewing.IsNone())
		{
			parser.NotifyService.Notify(enactor, "I can't see that here.");
			return new None();
		}

		var contents = parser.Database.GetContentsAsync(viewing).Result;

		var obj = viewing.Object()!;
		var ownerObj = obj.Owner()!.Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var location = obj.Key;
		var contentKeys = contents!.Select(x => x.Object()!.Name);
		var exitKeys = parser.Database.GetExitsAsync(obj.DBRef).Result?.FirstOrDefault();
		var description = parser.Database.GetAttributeAsync(obj.DBRef, "DESCRIBE").Result?.FirstOrDefault();

		parser.NotifyService.Notify(enactor, $"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(name))}" +
		                                     $"(#{obj.DBRef.Number}{string.Join(string.Empty, obj.Flags().Select(x => x.Symbol))})");
		parser.NotifyService.Notify(enactor, $"Type: {obj.Type} Flags: {string.Join(" ",obj.Flags().Select(x => x.Name))}");
		parser.NotifyService.Notify(enactor, $"{description?.Value ?? "There is nothing to see here."}");
		parser.NotifyService.Notify(enactor, $"Owner: {MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(ownerName))}" +
		                                     $"(#{obj.DBRef.Number}{string.Join(string.Empty, ownerObj.Flags().Select(x => x.Symbol))})");
		// TODO: Zone & Money
		parser.NotifyService.Notify(enactor, $"Parent: {obj.Parent()?.Name ?? "*NOTHING*"}");
		// TODO: LOCK LIST
		parser.NotifyService.Notify(enactor, $"Powers: {string.Join(" ",obj.Powers().Select(x => x.Name))}");
		// TODO: Channels
		// TODO: Warnings Checked
		
		// TODO: Match proper date format: Mon Feb 26 18:05:10 2007
		parser.NotifyService.Notify(enactor, $"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime).ToString("F")}");
		
		foreach(var attr in obj.Attributes())
		{
			parser.NotifyService.Notify(enactor, $"{attr.Name}: {attr.Value}");
		}

		// TODO: Attributes and Contents.

		// TODO: Proper carry format.
		parser.NotifyService.Notify(enactor, $"Carrying: {string.Join(Environment.NewLine, contentKeys)}");
		if(!viewing.IsT1)
		{ 
			// TODO: Proper Format.
			parser.NotifyService.Notify(enactor, $"Home: {viewing.WithoutNone().MinusRoom().Home().Object().Name}");
			parser.NotifyService.Notify(enactor, $"Location: {viewing.WithoutNone().MinusRoom().Location().Object().Name}");
		}
		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "@PEMIT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
	public static Option<CallState> PEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (args.Count < 2)
		{
			return new CallState(string.Empty);
		}

		var notification = args[1]!.Message!.ToString();
		var targetListText = MModule.plainText(args[0]!.Message!);
		var nameListTargets = Functions.Functions.NameList(targetListText);
		
		var enactor = parser.Database.GetObjectNode(parser.CurrentState.Executor!.Value).WithoutNone();

		foreach(var target in nameListTargets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var locateTarget = parser.LocateService.Locate(parser, enactor, enactor, targetString, Library.Services.LocateFlags.All);

			if (locateTarget.IsNone())
			{
				parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
			}
			else if(locateTarget.IsError())
			{
				parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, locateTarget.AsT5.Value);
			}
			else
			{
				parser.NotifyService.Notify(locateTarget.WithoutError().WithoutNone().Object().DBRef, notification);
			}
		}

		return new None();
	}
}
