﻿using OneOf.Monads;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "WHO", Behavior = Definitions.CommandBehavior.SOCKET, MinArgs = 0, MaxArgs = 1)]
		public static Option<CallState> WHO(Parser parser, SharpCommandAttribute _2)
		{
			_ = parser.State;
			return new None();
		}
	}
}
