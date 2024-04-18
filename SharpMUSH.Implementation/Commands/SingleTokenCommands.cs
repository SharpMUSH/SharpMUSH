using OneOf.Monads;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "]", Behavior = Definitions.CommandBehavior.SingleToken | Definitions.CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1)]
		public static Option<CallState> NoParse(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			// TODO: Notify others in the room.
			parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, parser.CurrentState.Arguments[0].ToString());

			return new CallState(string.Empty);
		}

		[SharpCommand(Name = "&", Behavior = Definitions.CommandBehavior.SingleToken | Definitions.CommandBehavior.NoParse | Definitions.CommandBehavior.EqSplit, MinArgs = 1, MaxArgs = 2)]
		public static Option<CallState> Set_Attrib_Ampersand(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			// This will come in as arg[0] = <attr> <object> and arg[1] as [value]
			throw new NotImplementedException();
		}
	}
}
