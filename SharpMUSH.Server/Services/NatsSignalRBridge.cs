using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;
using System.Text.Json;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Server.Services;

/// <summary>
/// IHostedService that bridges NATS events to SignalR hub clients.
/// Subscribes to portal.* NATS subjects and fans out via SignalR groups.
/// </summary>
public class NatsSignalRBridge : IHostedService
{
	private readonly IHubContext<PortalHub, IPortalHubClient> _hubContext;
	private readonly ILogger<NatsSignalRBridge> _logger;
	private readonly string _natsUrl;
	private NatsConnection? _connection;
	private readonly CancellationTokenSource _cancellation = new();

	public NatsSignalRBridge(
		IHubContext<PortalHub, IPortalHubClient> hubContext,
		ILogger<NatsSignalRBridge> logger,
		NatsOptions natsOptions)
	{
		_hubContext = hubContext;
		_logger = logger;
		_natsUrl = natsOptions.Url ?? "nats://localhost:4222";
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("[NatsSignalRBridge] Starting, connecting to NATS at {NatsUrl}", _natsUrl);

			var opts = new NatsOpts
			{
				Url = _natsUrl,
				ConnectTimeout = TimeSpan.FromSeconds(10),
				RequestTimeout = TimeSpan.FromSeconds(5),
			};

			_connection = new NatsConnection(opts);
			await _connection.ConnectAsync();

			_logger.LogInformation("[NatsSignalRBridge] Connected to NATS");

			// Subscribe to each portal.* subject
			_ = SubscribeToPortalPresenceAsync(cancellationToken);
			_ = SubscribeToPortalSceneLiveAsync(cancellationToken);
			_ = SubscribeToPortalWikiChangesAsync(cancellationToken);
			_ = SubscribeToPortalMailAsync(cancellationToken);
			_ = SubscribeToPortalNotifyAsync(cancellationToken);

			_logger.LogInformation("[NatsSignalRBridge] Subscribed to all portal.* subjects");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsSignalRBridge] Failed to start");
			throw;
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("[NatsSignalRBridge] Stopping");
		_cancellation.Cancel();

		if (_connection != null)
		{
			await _connection.DisposeAsync();
		}
	}

	private async Task SubscribeToPortalPresenceAsync(CancellationToken ct)
	{
		try
		{
			if (_connection == null)
				return;

			await foreach (var msg in _connection.SubscribeAsync("portal.presence", cancellationToken: ct))
			{
				try
				{
					var json = System.Text.Encoding.UTF8.GetString(msg.Data.Span);
					var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (root.TryGetProperty("sceneId", out var sceneIdEl) &&
					    root.TryGetProperty("characterName", out var charNameEl) &&
					    root.TryGetProperty("action", out var actionEl))
					{
						var sceneId = sceneIdEl.GetString() ?? "";
						var characterName = charNameEl.GetString() ?? "";
						var action = actionEl.GetString() ?? "";

						await _hubContext.Clients
							.Group($"scene_{sceneId}")
							.OnPresenceChanged(sceneId, characterName, action);

						_logger.LogDebug(
							"[NatsSignalRBridge] Broadcast presence change: scene={SceneId}, char={CharName}, action={Action}",
							sceneId, characterName, action);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[NatsSignalRBridge] Error processing portal.presence message");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsSignalRBridge] Error in portal.presence subscription");
		}
	}

	private async Task SubscribeToPortalSceneLiveAsync(CancellationToken ct)
	{
		try
		{
			if (_connection == null)
				return;

			await foreach (var msg in _connection.SubscribeAsync("portal.scene.live", cancellationToken: ct))
			{
				try
				{
					var json = System.Text.Encoding.UTF8.GetString(msg.Data.Span);
					var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (root.TryGetProperty("sceneId", out var sceneIdEl) &&
					    root.TryGetProperty("characterName", out var charNameEl) &&
					    root.TryGetProperty("pose", out var poseEl))
					{
						var sceneId = sceneIdEl.GetString() ?? "";
						var characterName = charNameEl.GetString() ?? "";
						var pose = poseEl.GetString() ?? "";

						await _hubContext.Clients
							.Group($"scene_{sceneId}")
							.OnPoseReceived(sceneId, characterName, pose);

						_logger.LogDebug(
							"[NatsSignalRBridge] Broadcast pose: scene={SceneId}, char={CharName}",
							sceneId, characterName);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[NatsSignalRBridge] Error processing portal.scene.live message");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsSignalRBridge] Error in portal.scene.live subscription");
		}
	}

	private async Task SubscribeToPortalWikiChangesAsync(CancellationToken ct)
	{
		try
		{
			if (_connection == null)
				return;

			await foreach (var msg in _connection.SubscribeAsync("portal.wiki.changes", cancellationToken: ct))
			{
				try
				{
					var json = System.Text.Encoding.UTF8.GetString(msg.Data.Span);
					var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (root.TryGetProperty("pageName", out var pageNameEl) &&
					    root.TryGetProperty("updatedBy", out var updatedByEl) &&
					    root.TryGetProperty("timestamp", out var tsEl))
					{
						var pageName = pageNameEl.GetString() ?? "";
						var updatedBy = updatedByEl.GetString() ?? "";
						var timestamp = tsEl.GetInt64();

						await _hubContext.Clients
							.Group("wiki")
							.OnWikiPageUpdated(pageName, updatedBy, timestamp);

						_logger.LogDebug(
							"[NatsSignalRBridge] Broadcast wiki update: page={PageName}, updatedBy={UpdatedBy}",
							pageName, updatedBy);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[NatsSignalRBridge] Error processing portal.wiki.changes message");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsSignalRBridge] Error in portal.wiki.changes subscription");
		}
	}

	private async Task SubscribeToPortalMailAsync(CancellationToken ct)
	{
		try
		{
			if (_connection == null)
				return;

			await foreach (var msg in _connection.SubscribeAsync("portal.mail", cancellationToken: ct))
			{
				try
				{
					var json = System.Text.Encoding.UTF8.GetString(msg.Data.Span);
					var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (root.TryGetProperty("accountId", out var accountIdEl) &&
					    root.TryGetProperty("mailId", out var mailIdEl) &&
					    root.TryGetProperty("fromCharacter", out var fromCharEl) &&
					    root.TryGetProperty("subject", out var subjectEl) &&
					    root.TryGetProperty("timestamp", out var tsEl))
					{
						var accountId = accountIdEl.GetString() ?? "";
						var mailId = mailIdEl.GetString() ?? "";
						var fromCharacter = fromCharEl.GetString() ?? "";
						var subject = subjectEl.GetString() ?? "";
						var timestamp = tsEl.GetString() ?? "";

						await _hubContext.Clients
							.Group($"mail_{accountId}")
							.OnMailReceived(mailId, fromCharacter, subject, timestamp);

						_logger.LogDebug(
							"[NatsSignalRBridge] Broadcast mail: accountId={AccountId}, from={FromChar}",
							accountId, fromCharacter);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[NatsSignalRBridge] Error processing portal.mail message");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsSignalRBridge] Error in portal.mail subscription");
		}
	}

	private async Task SubscribeToPortalNotifyAsync(CancellationToken ct)
	{
		try
		{
			if (_connection == null)
				return;

			await foreach (var msg in _connection.SubscribeAsync("portal.notify", cancellationToken: ct))
			{
				try
				{
					var json = System.Text.Encoding.UTF8.GetString(msg.Data.Span);
					var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (root.TryGetProperty("message", out var messageEl) &&
					    root.TryGetProperty("type", out var typeEl))
					{
						var message = messageEl.GetString() ?? "";
						var type = typeEl.GetString() ?? "info";

						// Broadcast to presence group (connected clients)
						await _hubContext.Clients
							.Group("presence")
							.OnNotification(message, type);

						_logger.LogDebug("[NatsSignalRBridge] Broadcast notification: {Message}", message);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[NatsSignalRBridge] Error processing portal.notify message");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsSignalRBridge] Error in portal.notify subscription");
		}
	}
}
