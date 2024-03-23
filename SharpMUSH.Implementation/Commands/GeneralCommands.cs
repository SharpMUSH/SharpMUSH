using OneOf.Monads;
using SharpMUSH.Library.Models;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
		public static Option<CallState> Think(Parser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@PEMIT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
		public static Option<CallState> PEmit(Parser parser, SharpCommandAttribute _2)
		{
			var args = parser.CurrentState.Arguments;

			if (args.Count < 2)
			{
				return new CallState(string.Empty);
			}

			var notification = args[1]!.Message!.ToString();
			var target = MModule.plainText(args[0]!.Message!);
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
