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
			var fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
			var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
			var players = everyone.Select(player => {
				var name = parser.Database.GetBaseObjectNode(player.Ref!.Value).GetAwaiter().GetResult();
				var onFor = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(player.Metadata["ConnectionStartTime"]));
				var idleFor = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(player.Metadata["LastConnectionSignal"]));
				return string.Format(
					fmt,
					name!.Name,
					Functions.Functions.TimeString(onFor, accuracy: 3),
					Functions.Functions.TimeString(idleFor),
					"Nothing");
			});
			var footer = $"{everyone.Count()} players logged in.";

			var message = $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, players)}{Environment.NewLine}{footer}";

			parser.NotifyService.Notify(handle: parser.CurrentState.Handle!, what: message).Wait();

			return new None();
		}
	}
}
