using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Broadcasts GAME:-prefixed messages to all connected players.
/// Mirrors PennMUSH's broadcast() / flag_broadcast() from src/bsd.c.
/// </summary>
public class GameBroadcastService(
	IConnectionService connectionService,
	INotifyService notifyService,
	IMediator mediator) : IGameBroadcastService
{
	/// <inheritdoc />
	public async ValueTask BroadcastAsync(string message)
	{
		var loggedIn = connectionService.GetAll().Where(conn => conn.State == IConnectionService.ConnectionState.LoggedIn);
		await foreach (var conn in loggedIn)
		{
			await notifyService.Notify(conn.Handle, message);
		}
	}

	/// <inheritdoc />
	public ValueTask BroadcastToFlagAsync(string flagName, string message)
		=> BroadcastToFlagAsync(null, flagName, message);

	/// <inheritdoc />
	public async ValueTask BroadcastToFlagAsync(IReadOnlyCollection<string>? anyOfFlags, string requiredFlag, string message)
	{
		// One decision per player, however many connections it has: its flags are read once.
		var decided = new Dictionary<DBRef, bool>();
		await foreach (var conn in connectionService.GetAll())
		{
			if (conn.State != IConnectionService.ConnectionState.LoggedIn || conn.Ref is null)
			{
				continue;
			}

			try
			{
				if (!decided.TryGetValue(conn.Ref.Value, out var hears))
				{
					if (await mediator.Send(new GetObjectNodeQuery(conn.Ref.Value)) is not AnySharpObject player)
					{
						continue;
					}

					var flags = await player.ReadFlagsAsync();
					hears = (anyOfFlags is null || anyOfFlags.Any(flags.Has)) && flags.Has(requiredFlag);
					decided[conn.Ref.Value] = hears;
				}

				if (hears)
				{
					await notifyService.Notify(conn.Handle, message);
				}
			}
			catch
			{
				// Skip connections where we can't resolve the player object.
				// This is a best-effort broadcast - a single bad connection
				// should not prevent other players from receiving the message.
			}
		}
	}

	/// <inheritdoc />
	public async ValueTask BroadcastShutdownAsync(string adminName, bool isReboot)
	{
		var message = isReboot
			? string.Format(ErrorMessages.Notifications.GameRebootBy, adminName)
			: string.Format(ErrorMessages.Notifications.GameShutdownBy, adminName);

		await BroadcastAsync(message);
	}
}
