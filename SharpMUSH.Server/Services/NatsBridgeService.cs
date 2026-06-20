using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Marker interface for the NATS-to-SignalR bridge background service.
/// Exposed for testing via <see cref="NatsBridgeService"/>.
/// </summary>
public interface INatsBridgeService : IHostedService;

/// <summary>
/// Background service that subscribes to game-output NATS subjects and
/// forwards incoming messages to the appropriate SignalR groups.
///
/// Subjects consumed:
///   "game.output.{characterDbref}" → SignalR group "char:{characterDbref}"
///   "game.room.{roomDbref}"        → SignalR group "room:{roomDbref}"
/// The "game.scene.{sceneId}" → "scene:{sceneId}" leg moved into the Scene plugin (Phase 5) as an
/// IBridgeSubscriptionSource, run here alongside the built-ins via the catalog's BridgeSources.
///
/// Uses core NATS subscriptions (not JetStream) because these are transient,
/// targeted delivery messages — not persistent queue messages.
/// </summary>
public sealed class NatsBridgeService : BackgroundService, INatsBridgeService
{
	private readonly IHubContext<GameHub, IGameHubClient> _hubContext;
	// Untyped hub context handed to plugin bridge sources. IBridgeSubscriptionSource.RunAsync receives it as
	// object and casts to the non-generic IHubContext to SendAsync(<method-name>, ...). IHubContext<GameHub,T>
	// (strongly-typed) does NOT implement the non-generic IHubContext, so we must pass IHubContext<GameHub>.
	private readonly IHubContext<GameHub> _pluginHubContext;
	private readonly NatsOptions _natsOptions;
	private readonly ILogger<NatsBridgeService> _logger;
	private readonly PluginCatalog _pluginCatalog;

	public NatsBridgeService(
		IHubContext<GameHub, IGameHubClient> hubContext,
		IHubContext<GameHub> pluginHubContext,
		NatsOptions natsOptions,
		PluginCatalog pluginCatalog,
		ILogger<NatsBridgeService> logger)
	{
		_hubContext = hubContext;
		_pluginHubContext = pluginHubContext;
		_natsOptions = natsOptions;
		_pluginCatalog = pluginCatalog;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// W-6: Wrap the connect+subscribe loop in an exponential-backoff retry
		// so the service recovers from transient NATS connection drops without
		// requiring a full process restart.
		var delay = 2;

		while (!stoppingToken.IsCancellationRequested)
		{
			NatsConnection? nats = null;
			try
			{
				_logger.LogInformation("[NatsBridge] Connecting to NATS at {Url}", _natsOptions.Url);
				nats = new NatsConnection(new NatsOpts { Url = _natsOptions.Url });
				await nats.ConnectAsync();
				_logger.LogInformation("[NatsBridge] Connected. Subscribing to game.output.* and game.room.* (game.scene.* is now a plugin bridge leg)");
				delay = 2; // reset backoff on successful connect

				// Phase 5: the game.scene.* leg moved into the Scene plugin as an IBridgeSubscriptionSource —
				// it is run below via the catalog's BridgeSources, alongside any other plugin bridge legs.
				var subscriptionTasks = new List<Task>
				{
					SubscribeOutputAsync(nats, stoppingToken),
					SubscribeRoomAsync(nats, stoppingToken)
				};

				// Phase 2a: run each plugin-contributed bridge subscription (IBridgeSubscriptionSource)
				// alongside the built-ins. Each is wrapped so a single failing subscription is logged and
				// cannot tear down the bridge loop (or the other subscriptions).
				foreach (var source in _pluginCatalog.BridgeSources)
				{
					subscriptionTasks.Add(RunBridgeSourceAsync(source, nats, stoppingToken));
				}

				await Task.WhenAll(subscriptionTasks);
				// All subscription loops exited cleanly (cancellation token triggered).
				break;
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation("[NatsBridge] Bridge service shutting down (cancelled).");
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[NatsBridge] Bridge error; retrying in {Delay}s", delay);
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				delay = Math.Min(delay * 2, 30);
			}
			finally
			{
				if (nats is not null)
					await nats.DisposeAsync();
			}
		}
	}

	/// <summary>
	/// Run one plugin-contributed bridge subscription, isolating any failure so it cannot tear down the
	/// other subscriptions or the bridge connect loop. Cancellation is allowed to propagate so the
	/// surrounding <see cref="Task.WhenAll(System.Collections.Generic.IEnumerable{Task})"/> unwinds cleanly.
	/// </summary>
	private async Task RunBridgeSourceAsync(
		SharpMUSH.Library.Plugins.IBridgeSubscriptionSource source,
		NatsConnection nats,
		CancellationToken ct)
	{
		try
		{
			await source.RunAsync(nats, _pluginHubContext, ct);
		}
		catch (OperationCanceledException)
		{
			// Bridge shutting down — let cancellation propagate to WhenAll.
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsBridge] Plugin bridge subscription '{Source}' faulted; it will not be retried until reconnect.",
				source.GetType().FullName);
		}
	}

	private async Task SubscribeOutputAsync(NatsConnection nats, CancellationToken ct)
	{
		// Subject wildcard: "game.output.*" — the last token is the character dbref.
		await foreach (var msg in nats.SubscribeAsync<GameOutputMessage>(
			"game.output.*",
			serializer: NatsJsonSerializer<GameOutputMessage>.Default,
			cancellationToken: ct))
		{
			if (msg.Data is null) continue;

			var dbref = msg.Data.CharacterDbref;
			var group = GameHub.CharacterGroupName(dbref);

			_logger.LogDebug("[NatsBridge] Forwarding GameOutputMessage for char:{Dbref} to group {Group}",
				dbref, group);

			try
			{
				await _hubContext.Clients.Group(group).ReceiveOutput(msg.Data);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "[NatsBridge] Error forwarding output message to group {Group}", group);
			}
		}
	}

	private async Task SubscribeRoomAsync(NatsConnection nats, CancellationToken ct)
	{
		// Subject wildcard: "game.room.*" — the last token is the room dbref.
		await foreach (var msg in nats.SubscribeAsync<RoomEventMessage>(
			"game.room.*",
			serializer: NatsJsonSerializer<RoomEventMessage>.Default,
			cancellationToken: ct))
		{
			if (msg.Data is null) continue;

			var dbref = msg.Data.RoomDbref;
			var group = GameHub.RoomGroupName(dbref);

			_logger.LogDebug("[NatsBridge] Forwarding RoomEventMessage for room:{Dbref} to group {Group}",
				dbref, group);

			try
			{
				await _hubContext.Clients.Group(group).ReceiveRoomEvent(msg.Data);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "[NatsBridge] Error forwarding room event to group {Group}", group);
			}
		}
	}
}
