using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
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
///
/// Uses core NATS subscriptions (not JetStream) because these are transient,
/// targeted delivery messages — not persistent queue messages.
/// </summary>
public sealed class NatsBridgeService : BackgroundService, INatsBridgeService
{
	private readonly IHubContext<GameHub, IGameHubClient> _hubContext;
	private readonly NatsOptions _natsOptions;
	private readonly ILogger<NatsBridgeService> _logger;

	public NatsBridgeService(
		IHubContext<GameHub, IGameHubClient> hubContext,
		NatsOptions natsOptions,
		ILogger<NatsBridgeService> logger)
	{
		_hubContext = hubContext;
		_natsOptions = natsOptions;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		NatsConnection? nats = null;
		try
		{
			_logger.LogInformation("[NatsBridge] Connecting to NATS at {Url}", _natsOptions.Url);
			nats = new NatsConnection(new NatsOpts { Url = _natsOptions.Url });
			await nats.ConnectAsync();
			_logger.LogInformation("[NatsBridge] Connected. Subscribing to game.output.* and game.room.*");

			var outputTask = SubscribeOutputAsync(nats, stoppingToken);
			var roomTask = SubscribeRoomAsync(nats, stoppingToken);

			await Task.WhenAll(outputTask, roomTask);
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("[NatsBridge] Bridge service shutting down (cancelled).");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NatsBridge] Bridge service encountered a fatal error.");
		}
		finally
		{
			if (nats is not null)
				await nats.DisposeAsync();
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
