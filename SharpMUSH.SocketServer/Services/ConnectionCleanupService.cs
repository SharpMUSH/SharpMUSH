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
		await Parallel.ForEachAsync(await stateStore.GetAllConnectionsAsync(cancellationToken),
			new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
			async (record, ct) =>
		{
			var (handle, data) = record;
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
			deadline.CancelAfter(TimeSpan.FromSeconds(10));
			try
			{
				var expiry = ResumeExpiry(data, grace);
				if (data.ConnectionType == "websocket"
					&& data.Metadata.GetValueOrDefault("ResumeRevoked") != "1"
					&& Guid.TryParseExact(data.Metadata.GetValueOrDefault("SessionId"), "N", out _)
					&& expiry > DateTimeOffset.UtcNow)
				{
					await pump.RestoreDormantAsync(data, expiry, deadline.Token);
					logger.LogInformation("Restored dormant browser session {Handle} until {Expiry}", handle, expiry);
				}
				else
				{
					await stateStore.RemoveConnectionAsync(handle, deadline.Token);
					if (data.Metadata.GetValueOrDefault("SessionId") is { Length: > 0 } sessionId)
						await bus.Publish(new SharpMUSH.Messaging.Messages.ConnectionClosedMessage(
							handle, DateTimeOffset.UtcNow, sessionId), deadline.Token);
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Could not recover persisted connection {Handle}; continuing startup", handle);
			}
		});
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
