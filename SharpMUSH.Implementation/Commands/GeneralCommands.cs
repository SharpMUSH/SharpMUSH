using OneOf.Monads;
using SharpMUSH.Database.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
		public static Option<CallState> Think(MUSHCodeParser parser, SharpCommandAttribute _2)
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
		public static Option<CallState> Look(MUSHCodeParser parser, SharpCommandAttribute _2)
		{
			// TODO: Consult CONFORMAT, DESCFORMAT, INAMEFORMAT, NAMEFORMAT, etc.
			
			var args = parser.CurrentState.Arguments;
			var executor = parser.CurrentState.Executor!.Value;
			OneOf.OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None> viewing;

			if (args.Count == 1)
			{
				// TODO: Find the object using Locate(), then give the results of that.
				viewing = new OneOf.Types.None();
				throw new NotImplementedException();
			}
			else
			{
				viewing = parser.Database.GetLocationAsync(executor, 1).Result;
				// Look at the current location.
			}

			if (viewing.IsT4)
			{
				parser.NotifyService.Notify(executor, "I can't see that here.");
			}
			
			var contents = parser.Database.GetContentsAsync(viewing).Result;

			var name = viewing.Object()!.Name;
			var location = viewing.Object()!.Key;
			var contentKeys = contents!.Select(x => x.Object()!.Key).Where( x => x.HasValue);

			parser.NotifyService.Notify(executor, $"Name: {name}");
			parser.NotifyService.Notify(executor, $"Location: {location}");
			parser.NotifyService.Notify(executor, $"Contents: {string.Join(Environment.NewLine, contentKeys)}");

			return new None();
		}

		[SharpCommand(Name = "@PEMIT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
		public static Option<CallState> PEmit(MUSHCodeParser parser, SharpCommandAttribute _2)
		{
			var args = parser.CurrentState.Arguments;

			if (args.Count < 2)
			{
				return new CallState(string.Empty);
			}

			var notification = args[1]!.Message!.ToString();
			var target = MModule.plainText(args[0]!.Message!);

			// TODO: Use Locate() here.
			var parsedTarget = Functions.Functions.ParseDBRef(target);
			
			if (parsedTarget.IsNone())
			{
				parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
			}
			else
			{
				parser.NotifyService.Notify(parsedTarget.AsT1.Value, notification);
			}

			return new None();
		}
	}
}
