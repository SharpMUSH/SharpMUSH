namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "]", Behavior = Definitions.CommandBehavior.SingleToken | Definitions.CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1)]
		public static CallState NoParse(Parser parser, SharpCommandAttribute _2)
		{
			return new CallState("");
		}
	}
}
