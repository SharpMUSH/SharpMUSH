using Mediator;
using SharpMUSH.Library.Definitions;
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
		await foreach (var conn in connectionService.GetAll())
		{
			if (conn.State == IConnectionService.ConnectionState.LoggedIn)
			{
				await notifyService.Notify(conn.Handle, message);
			}
		}
	}

	/// <inheritdoc />
	public ValueTask BroadcastToFlagAsync(string flagName, string message)
		=> BroadcastToFlagAsync(null, flagName, message);

	/// <inheritdoc />
	public async ValueTask BroadcastToFlagAsync(IReadOnlyCollection<string>? anyOfFlags, string requiredFlag, string message)
	{
		await foreach (var conn in connectionService.GetAll())
		{
			if (conn.State != IConnectionService.ConnectionState.LoggedIn || conn.Ref is null)
			{
				continue;
			}

			try
			{
				var playerResult = await mediator.Send(new GetObjectNodeQuery(conn.Ref.Value));
				if (playerResult.IsNone)
				{
					continue;
				}

				var player = playerResult.Known;

				if (anyOfFlags is not null)
				{
					var hasAny = false;
					foreach (var flag in anyOfFlags)
					{
						if (await player.HasFlag(flag))
						{
							hasAny = true;
							break;
						}
					}
					if (!hasAny)
					{
						continue;
					}
				}

				if (await player.HasFlag(requiredFlag))
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
