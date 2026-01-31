using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Server.Handlers;

/// <summary>
/// Handles object flag changes and syncs output preferences to ConnectionServer
/// when ANSI/COLOR/XTERM256 flags are modified
/// </summary>
public class ObjectFlagChangeHandler(
	ILogger<ObjectFlagChangeHandler> logger,
	IConnectionService connectionService,
	IMessageBus messageBus)
	: INotificationHandler<ObjectFlagChangedNotification>
{
	private static readonly HashSet<string> OutputPreferenceFlags = new(StringComparer.OrdinalIgnoreCase)
	{
		"ANSI",
		"COLOR",
		"XTERM256"
	};

	public async ValueTask Handle(ObjectFlagChangedNotification notification, CancellationToken cancellationToken)
	{
		// Only sync if this is one of the output preference flags
		if (!OutputPreferenceFlags.Contains(notification.FlagName))
		{
			return;
		}

		// Find all connections for this player
		var connections = await connectionService.GetAll()
			.Where(c => c.Ref == notification.Target.Object().DBRef)
			.ToListAsync(cancellationToken);

		if (connections.Count == 0)
		{
			logger.LogDebug("No active connections for player {Player} to sync {Flag} flag change",
				notification.Target.Object().DBRef, notification.FlagName);
			return;
		}

		// Query all output preference flags from the player object
		var player = notification.Target.Object();
		var ansiEnabled = await player.Flags.Value.AnyAsync(f =>
			string.Equals(f.Name, "ANSI", StringComparison.OrdinalIgnoreCase), cancellationToken);
		var colorEnabled = await player.Flags.Value.AnyAsync(f =>
			string.Equals(f.Name, "COLOR", StringComparison.OrdinalIgnoreCase), cancellationToken);
		var xterm256Enabled = await player.Flags.Value.AnyAsync(f =>
			string.Equals(f.Name, "XTERM256", StringComparison.OrdinalIgnoreCase), cancellationToken);

		// Sync preferences to all connections for this player
		foreach (var connection in connections)
		{
			await messageBus.Publish(new UpdatePlayerPreferencesMessage(
				connection.Handle,
				ansiEnabled,
				colorEnabled,
				xterm256Enabled
			), cancellationToken);

			logger.LogInformation("Synced {Flag} flag change for player {Player} on handle {Handle}: ANSI={Ansi}, COLOR={Color}, XTERM256={Xterm}",
				notification.FlagName,
				notification.Target.Object().DBRef,
				connection.Handle,
				ansiEnabled,
				colorEnabled,
				xterm256Enabled);
		}
	}
}
