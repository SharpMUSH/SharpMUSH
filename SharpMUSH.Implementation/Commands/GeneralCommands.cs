using OneOf.Monads;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands
{
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
			var executor = parser.CurrentState.Executor!.Value;
			parser.NotifyService.Notify(executor, notification);

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
				var locate = Functions.Functions.Locate(parser, enactor, enactor, args[0]!.Message!.ToString(), Functions.Functions.LocateFlags.All);
				
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
			var contentKeys = contents!.Select(x => x.Object()!.Key);

			parser.NotifyService.Notify(enactor, $"Name: {name}");
			parser.NotifyService.Notify(enactor, $"Location: {location}");
			parser.NotifyService.Notify(enactor, $"Contents: {string.Join(Environment.NewLine, contentKeys)}");

			return new CallState(viewing.Object()!.DBRef.ToString());
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
			
			var executor = parser.Database.GetObjectNode(parser.CurrentState.Executor!.Value).WithoutNone();

			foreach(var target in nameListTargets)
			{
				var targetString = target.Match(dbref => dbref.ToString(), str => str);
				var locateTarget = Functions.Functions.Locate(parser, executor, executor, targetString, Functions.Functions.LocateFlags.All);

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
}
