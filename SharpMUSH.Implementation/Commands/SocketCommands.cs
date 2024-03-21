using OneOf.Monads;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "WHO", Behavior = Definitions.CommandBehavior.SOCKET, MinArgs = 0, MaxArgs = 1)]
		public static Option<CallState> WHO(Parser parser, SharpCommandAttribute _2)
		{
			_ = parser.State;
			var everyone = parser.ConnectionService.GetAll();
			var fmt = "{0:-18} {1:9} {2:4} {3:32}";
			var header = string.Format(fmt,"Player Name", "On For", "Idle", "Doing");
			var players = everyone.Select(player => string.Format(
				fmt, // It Errors here.
				parser.Database.GetBaseObjectNode(player.Ref!.Value).Result!.Name, 
				player.Metadata["ConnectionStartTime"], 
				player.Metadata["LastConnectionSignal"], "Nothing."));
			var footer = $"{everyone.Count()} players logged in.";

			var message = $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, players)}{Environment.NewLine}{footer}";

			parser.NotifyService.Notify(handle: parser.CurrentState.Handle, what: message).Wait();

			return new None();
		}
	}
}
