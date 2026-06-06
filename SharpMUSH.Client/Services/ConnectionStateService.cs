using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SignalRState = Microsoft.AspNetCore.SignalR.Client.HubConnectionState;
using LibraryState = SharpMUSH.Library.Services.Interfaces.HubConnectionState;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side SignalR connection manager.  Builds a connection to /hubs/game,
/// manages auto-reconnect (exponential back-off is applied inside
/// <see cref="IGameHubConnectionFactory"/>), and surfaces received messages
/// as events.
/// </summary>
public sealed class ConnectionStateService : IConnectionStateService, IAsyncDisposable
{
	private readonly IGameHubConnectionFactory _factory;
	private readonly ILogger<ConnectionStateService> _logger;

	private IGameHubConnection? _hub;
	private SignalRState _innerState = SignalRState.Disconnected;
	private readonly List<IDisposable> _subscriptions = [];

	public event Action? OnConnectionStateChanged;
	public event Action<GameOutputMessage>? OnOutputReceived;
	public event Action<RoomEventMessage>? OnRoomEventReceived;

	public ConnectionStateService(
		IGameHubConnectionFactory factory,
		ILogger<ConnectionStateService> logger)
	{
		_factory = factory;
		_logger = logger;
	}

	/// <inheritdoc/>
	public bool IsConnected => _innerState == SignalRState.Connected;

	/// <inheritdoc/>
	public LibraryState ConnectionState => MapState(_innerState);

	// ── Connect / Disconnect ─────────────────────────────────────────────────

	/// <inheritdoc/>
	public async Task ConnectAsync(string accessToken)
	{
		if (_hub is not null)
		{
			_logger.LogDebug("[ConnectionStateService] Already connected — ignoring ConnectAsync");
			return;
		}

		SetState(SignalRState.Connecting);
		_hub = _factory.Create(accessToken);

		_subscriptions.Add(_hub.On("ReceiveOutput", (GameOutputMessage msg) =>
		{
			_logger.LogDebug("[ConnectionStateService] ReceiveOutput: {Type}", msg.MessageType);
			OnOutputReceived?.Invoke(msg);
		}));

		_subscriptions.Add(_hub.On("ReceiveRoomEvent", (RoomEventMessage msg) =>
		{
			_logger.LogDebug("[ConnectionStateService] ReceiveRoomEvent: {EventType}", msg.EventType);
			OnRoomEventReceived?.Invoke(msg);
		}));

		_hub.Closed += ex =>
		{
			_logger.LogWarning(ex, "[ConnectionStateService] Hub closed");
			SetState(SignalRState.Disconnected);
			return Task.CompletedTask;
		};

		_hub.Reconnecting += ex =>
		{
			_logger.LogInformation(ex, "[ConnectionStateService] Hub reconnecting");
			SetState(SignalRState.Reconnecting);
			return Task.CompletedTask;
		};

		_hub.Reconnected += _ =>
		{
			_logger.LogInformation("[ConnectionStateService] Hub reconnected");
			SetState(SignalRState.Connected);
			return Task.CompletedTask;
		};

		try
		{
			await _hub.StartAsync();
			SetState(SignalRState.Connected);
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (hub state)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
		}
		catch (HubException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (hub error)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (unexpected)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
		}
	}

	/// <inheritdoc/>
	public async Task DisconnectAsync()
	{
		if (_hub is null) return;

		try
		{
			await _hub.StopAsync();
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogWarning(ex, "[ConnectionStateService] StopAsync threw during disconnect (hub state)");
		}
		catch (HubException ex)
		{
			_logger.LogWarning(ex, "[ConnectionStateService] StopAsync threw during disconnect (hub error)");
		}

		SetState(SignalRState.Disconnected);
		await DisposeHubAsync();
	}

	/// <inheritdoc/>
	public Task SendCommandAsync(string command)
	{
		if (_hub is null || !IsConnected)
			throw new InvalidOperationException("Not connected to the game hub.");

		return _hub.InvokeAsync("SendCommand", command);
	}

	// ── State helpers ────────────────────────────────────────────────────────

	private void SetState(SignalRState state)
	{
		_innerState = state;
		OnConnectionStateChanged?.Invoke();
	}

	private static LibraryState MapState(SignalRState inner) => inner switch
	{
		SignalRState.Disconnected => LibraryState.Disconnected,
		SignalRState.Connecting   => LibraryState.Connecting,
		SignalRState.Connected    => LibraryState.Connected,
		SignalRState.Reconnecting => LibraryState.Reconnecting,
		_                         => LibraryState.Disconnected,
	};

	// ── Disposal ─────────────────────────────────────────────────────────────

	private async Task DisposeHubAsync()
	{
		foreach (var sub in _subscriptions) sub.Dispose();
		_subscriptions.Clear();

		if (_hub is not null)
		{
			await _hub.DisposeAsync();
			_hub = null;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
		await DisposeHubAsync();
	}
}
