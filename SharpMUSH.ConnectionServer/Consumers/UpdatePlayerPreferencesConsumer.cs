using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes player preference update messages from MainProcess
/// </summary>
public class UpdatePlayerPreferencesConsumer(
	IConnectionServerService connectionService,
	ILogger<UpdatePlayerPreferencesConsumer> logger)
: IMessageConsumer<UpdatePlayerPreferencesMessage>
{
	public Task HandleAsync(UpdatePlayerPreferencesMessage message, CancellationToken cancellationToken = default)
	{
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received preference update for unknown connection handle: {Handle}", message.Handle);
			return Task.CompletedTask;
		}

		try
		{
			// Update the connection's preferences
			var updatedPreferences = new PlayerOutputPreferences(
				AnsiEnabled: message.AnsiEnabled,
				ColorEnabled: message.ColorEnabled,
				Xterm256Enabled: message.Xterm256Enabled
			);

			// Update in the connection service
			var success = connectionService.UpdatePreferences(message.Handle, updatedPreferences);

			if (success)
			{
				logger.LogInformation(
					"Updated preferences for connection {Handle}: ANSI={Ansi}, COLOR={Color}, XTERM256={Xterm}",
					message.Handle,
					message.AnsiEnabled,
					message.ColorEnabled,
					message.Xterm256Enabled);
			}
			else
			{
				logger.LogWarning("Failed to update preferences for connection {Handle}", message.Handle);
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error updating preferences for connection {Handle}", message.Handle);
		}

		return Task.CompletedTask;
	}
}
