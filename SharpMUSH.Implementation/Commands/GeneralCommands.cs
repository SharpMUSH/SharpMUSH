using SharpMUSH.Library.Models;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
		public static CallState Think(Parser parser, SharpCommandAttribute _2)
		{
			var args = parser.CurrentState.Arguments;

			if (args.Count < 1)
			{
				return new CallState(string.Empty);
			}

			var notification = args[0]!.Message!.ToString();
			var executor = parser.CurrentState.Executor;
			parser.NotifyService.Notify(executor, notification);

			return new CallState("");
		}

		[SharpCommand(Name = "@PEMIT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
		public static CallState PEmit(Parser parser, SharpCommandAttribute _2)
		{
			var args = parser.CurrentState.Arguments;

			if (args.Count < 2)
			{
				return new CallState(string.Empty);
			}

			var notification = args[1]!.Message!.ToString();
			var target = MModule.plainText(args[0]!.Message!);
			var parsedTarget = Functions.Functions.ParseDBRef(target);
			
			if (parsedTarget.IsT0)
			{
				parser.NotifyService.Notify(parsedTarget.AsT0, notification);
			}
			else
			{
				parser.NotifyService.Notify(parser.CurrentState.Executor, "I can't see that here.");
			}

			return new CallState("");
		}
	}
}
