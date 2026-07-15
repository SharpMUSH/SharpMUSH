using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Client.Models;
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
public sealed class ConnectionStateService : IConnectionStateService, ISceneHubControl, IAsyncDisposable
{
	private readonly IGameHubConnectionFactory _factory;
	private readonly ILogger<ConnectionStateService> _logger;

	private IGameHubConnection? _hub;
	// Phase 9: scene realtime now rides a SEPARATE connection to the plugin-owned hub at /hubs/scene
	// (ReceiveSceneMessage + JoinScene/LeaveScene), not the GameHub connection.
	private IGameHubConnection? _sceneHub;
	private SignalRState _innerState = SignalRState.Disconnected;
	private readonly List<IDisposable> _subscriptions = [];

	public event Action? OnConnectionStateChanged;
	public event Action<GameOutputMessage>? OnOutputReceived;
	public event Action<RoomEventMessage>? OnRoomEventReceived;
	public event Action<SceneEventMessage>? OnSceneEventReceived;
	public event Action? OnPluginsChanged;

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

	/// <inheritdoc/>
	public async Task ConnectAsync()
	{
		if (_hub is not null)
		{
			_logger.LogDebug("[ConnectionStateService] Already connected — ignoring ConnectAsync");
			return;
		}

		SetState(SignalRState.Connecting);
		_hub = _factory.Create();

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

		// Generic plugins-changed signal: the server unloaded/reloaded a plugin DLL. Surface it so the portal
		// shell can force a hard browser refresh (the only way to reclaim a browser-loaded component assembly).
		_subscriptions.Add(_hub.On("ReceivePluginsChanged", () =>
		{
			_logger.LogInformation("[ConnectionStateService] ReceivePluginsChanged — a plugin changed; signalling a reload");
			OnPluginsChanged?.Invoke();
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

			// Phase 9: open the separate scene realtime connection (/hubs/scene). Best-effort — a scene-hub
			// failure must not break the primary game connection; scene pages simply receive no live events.
			await ConnectSceneHubAsync();
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
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (network)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
		}
		catch (TaskCanceledException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (cancelled)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
		}
		catch (OperationCanceledException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (operation cancelled)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed with unexpected exception");
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

	/// <summary>
	/// Opens the separate scene realtime connection and wires <c>ReceiveSceneMessage</c> to
	/// <see cref="OnSceneEventReceived"/>. No-ops when the factory provides no scene hub URL (e.g. the
	/// test factory). Best-effort: any failure is swallowed so the primary game connection is unaffected.
	/// </summary>
	private async Task ConnectSceneHubAsync()
	{
		if (_sceneHub is not null) return;

		var sceneHub = _factory.CreateScene();
		if (sceneHub is null) return; // No scene hub configured — scene realtime simply stays inert.

		sceneHub.On("ReceiveSceneMessage", (SceneEventMessage msg) =>
		{
			_logger.LogDebug("[ConnectionStateService] ReceiveSceneMessage: {EventType}", msg.EventType);
			OnSceneEventReceived?.Invoke(msg);
		});

		try
		{
			await sceneHub.StartAsync();
			_sceneHub = sceneHub;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] Scene hub StartAsync failed; scene realtime disabled");
			await sceneHub.DisposeAsync();
		}
	}

	/// <inheritdoc/>
	public Task JoinSceneAsync(string sceneId)
	{
		if (_sceneHub is null || _sceneHub.State != SignalRState.Connected) return Task.CompletedTask;
		return _sceneHub.InvokeAsync("JoinScene", sceneId);
	}

	/// <inheritdoc/>
	public Task LeaveSceneAsync(string sceneId)
	{
		if (_sceneHub is null || _sceneHub.State != SignalRState.Connected) return Task.CompletedTask;
		return _sceneHub.InvokeAsync("LeaveScene", sceneId);
	}

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

	private async Task DisposeHubAsync()
	{
		foreach (var sub in _subscriptions) sub?.Dispose();
		_subscriptions.Clear();

		if (_hub is not null)
		{
			await _hub.DisposeAsync();
			_hub = null;
		}

		if (_sceneHub is not null)
		{
			await _sceneHub.DisposeAsync();
			_sceneHub = null;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
		await DisposeHubAsync();
	}
}
