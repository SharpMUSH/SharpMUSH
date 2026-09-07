using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Restores resumable browser sessions and removes records with no surviving transport.</summary>
public class ConnectionCleanupService(
	IConnectionStateStore stateStore,
	ILogger<ConnectionCleanupService> logger,
	ConnectionPump pump,
	IConfiguration configuration,
	SharpMUSH.Messaging.Abstractions.IMessageBus bus) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var grace = TimeSpan.FromSeconds(configuration.GetValue("Session:GraceSeconds", 120.0));
		foreach (var (handle, data) in await stateStore.GetAllConnectionsAsync(cancellationToken))
		{
			var expiry = ResumeExpiry(data, grace);
			if (data.ConnectionType == "websocket"
				&& data.Metadata.GetValueOrDefault("ResumeRevoked") != "1"
				&& Guid.TryParseExact(data.Metadata.GetValueOrDefault("SessionId"), "N", out _)
				&& expiry > DateTimeOffset.UtcNow)
			{
				await pump.RestoreDormantAsync(data, expiry, cancellationToken);
				logger.LogInformation("Restored dormant browser session {Handle} until {Expiry}", handle, expiry);
			}
			else
			{
				await stateStore.RemoveConnectionAsync(handle, cancellationToken);
				if (data.Metadata.GetValueOrDefault("SessionId") is { Length: > 0 } sessionId)
					await bus.Publish(new SharpMUSH.Messaging.Messages.ConnectionClosedMessage(
						handle, DateTimeOffset.UtcNow, sessionId), cancellationToken);
			}
		}
	}

	internal static DateTimeOffset ResumeExpiry(ConnectionStateData data, TimeSpan grace)
	{
		if (long.TryParse(data.Metadata.GetValueOrDefault("ResumeExpiresAt"), out var deadline) && deadline > 0)
			return DateTimeOffset.FromUnixTimeMilliseconds(deadline);
		// Allow one heartbeat interval in addition to grace after an abrupt owner loss.
		return data.LastSeen.Add(grace).AddMinutes(1);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
