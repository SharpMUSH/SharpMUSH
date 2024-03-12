namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "THINK", Behavior = Definitions.CommandBehavior.Default, MinArgs = 0, MaxArgs = 1)]
		public static CallState Think(Parser parser, SharpCommandAttribute _2)
		{
			var args = parser.State.Peek().Arguments;

			if (args.Count < 1)
			{
				return new CallState(string.Empty);
			}

			var notification = args[0]!.Message!.ToString();
			var executor = parser.State.Peek().Executor;
			parser.NotifyService.Notify(executor, notification);

			return new CallState("");
		}
	}
}
