using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that listens to connection state changes and triggers appropriate events.
/// </summary>
public class EventTriggerService(
	IConnectionService connectionService,
	IEventService eventService,
	IMUSHCodeParser parser,
	ILogger<EventTriggerService> logger) 
	: IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		connectionService.ListenState(HandleConnectionStateChange);
		logger.LogInformation("Event trigger service started");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Event trigger service stopped");
		return Task.CompletedTask;
	}

	private void HandleConnectionStateChange(
		(long Handle, DBRef? Ref, IConnectionService.ConnectionState OldState, IConnectionService.ConnectionState NewState) change)
	{
		var (handle, dbRef, oldState, newState) = change;
		
		// Trigger SOCKET`CONNECT when a new socket connects
		if (oldState == IConnectionService.ConnectionState.None && 
		    newState == IConnectionService.ConnectionState.Connected)
		{
			// Get connection info
			var connectionData = connectionService.Get(handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip) 
					? ip : "unknown";
				
				// Trigger SOCKET`CONNECT event
				// Args: descriptor, ip
				_ = Task.Run(async () =>
				{
					try
					{
						await eventService.TriggerEventAsync(
							parser,
							"SOCKET`CONNECT",
							null, // System event (no enactor)
							handle.ToString(),
							ipAddress);
					}
					catch (Exception ex)
					{
						logger.LogError(ex, "Error triggering SOCKET`CONNECT event");
					}
				});
			}
		}
		
		// Trigger SOCKET`DISCONNECT when a socket disconnects
		if (newState == IConnectionService.ConnectionState.Disconnected)
		{
			// Get connection info before it's removed
			var connectionData = connectionService.Get(handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip) 
					? ip : "unknown";
				
				// Calculate statistics
				var connectedTime = connectionData.Connected?.TotalSeconds.ToString("F0") ?? "0";
				var bytesRecv = connectionData.Metadata.TryGetValue("BytesReceived", out var recv) ? recv : "0";
				var bytesSent = connectionData.Metadata.TryGetValue("BytesSent", out var sent) ? sent : "0";
				var commandCount = connectionData.Metadata.TryGetValue("CommandCount", out var count) ? count : "0";
				
				// Trigger SOCKET`DISCONNECT event
				// Args: former descriptor, former ip, cause of disconnection, recv bytes/sent bytes/command count
				_ = Task.Run(async () =>
				{
					try
					{
						await eventService.TriggerEventAsync(
							parser,
							"SOCKET`DISCONNECT",
							null, // System event (no enactor)
							handle.ToString(),
							ipAddress,
							"disconnect", // cause - simplified for now
							$"{bytesRecv}/{bytesSent}/{commandCount}");
					}
					catch (Exception ex)
					{
						logger.LogError(ex, "Error triggering SOCKET`DISCONNECT event");
					}
				});
			}
		}
	}
}
