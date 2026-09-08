using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes player preference update messages from MainProcess
/// </summary>
public class UpdatePlayerPreferencesConsumer(
	IConnectionServerService connectionService,
	ILogger<UpdatePlayerPreferencesConsumer> logger)
	: IMessageConsumer<UpdatePlayerPreferencesMessage>,
		IMessageConsumer<ClearPlayerOutputPreferencesMessage>,
		IMessageConsumer<UpdateColorStyleMessage>
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
			var existingPreferences = connection.Preferences;
			var updatedPreferences = new PlayerOutputPreferences(
				AnsiEnabled: message.AnsiEnabled,
				ColorEnabled: message.ColorEnabled,
				Xterm256Enabled: message.Xterm256Enabled,
				TruecolorEnabled: message.TruecolorEnabled,
				Locale: existingPreferences?.Locale ?? "en"
			);

			var success = connectionService.UpdatePreferences(message.Handle, updatedPreferences);

			if (success)
			{
				logger.LogInformation(
					"Updated preferences for connection {Handle}: ANSI={Ansi}, COLOR={Color}, XTERM256={Xterm}, TRUECOLOR={Truecolor}",
					message.Handle,
					message.AnsiEnabled,
					message.ColorEnabled,
					message.Xterm256Enabled,
					message.TruecolorEnabled);
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

	public Task HandleAsync(UpdateColorStyleMessage message, CancellationToken cancellationToken = default)
	{
		if (connectionService.UpdateColorStyle(message.Handle, message.Style))
		{
			logger.LogInformation("Colour style for connection {Handle} is now {Style}",
				message.Handle, message.Style ?? "auto");
		}
		else
		{
			logger.LogWarning("Could not set colour style for unknown connection handle: {Handle}", message.Handle);
		}

		return Task.CompletedTask;
	}

	public Task HandleAsync(ClearPlayerOutputPreferencesMessage message, CancellationToken cancellationToken = default)
	{
		if (connectionService.ClearPreferences(message.Handle))
		{
			logger.LogInformation("Cleared player output preferences for connection {Handle}", message.Handle);
		}
		else
		{
			logger.LogWarning("Could not clear preferences for unknown connection handle: {Handle}", message.Handle);
		}

		return Task.CompletedTask;
	}
}
