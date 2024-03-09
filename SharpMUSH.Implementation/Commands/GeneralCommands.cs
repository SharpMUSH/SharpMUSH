namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "THINK", Behavior = Definitions.CommandBehavior.Undefined, MinArgs = 0, MaxArgs = 1)]
		public static CallState Think(Parser parser, SharpCommandAttribute _2)
		{
			var args = parser.State.Peek().Arguments;

			if (args.Length < 1)
			{
				return new CallState(string.Empty);
			}
			
			return parser.FunctionParse(parser.State.Peek().Arguments[1].Message!.ToString())!;
		}
	}
}
