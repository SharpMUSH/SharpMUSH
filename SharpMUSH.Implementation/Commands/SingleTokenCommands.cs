namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "]", Behavior = Definitions.CommandBehavior.SingleToken | Definitions.CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1)]
		public static CallState NoParse(Parser parser, SharpCommandAttribute _2)
		{
			// TODO: Notify others in the room.
			parser.NotifyService.Notify(parser.CurrentState().Executor, parser.CurrentState().Arguments[0].ToString());

			return new CallState(string.Empty);
		}

		[SharpCommand(Name = "&", Behavior = Definitions.CommandBehavior.SingleToken | Definitions.CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1)]
		public static CallState Set_Attrib_Ampersand(Parser parser, SharpCommandAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
