using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SignalRState = Microsoft.AspNetCore.SignalR.Client.HubConnectionState;
using LibraryState = SharpMUSH.Library.Services.Interfaces.HubConnectionState;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side SignalR connection manager.  Builds a connection to /hubs/game,
/// manages auto-reconnect (exponential back-off is applied inside
/// <see cref="IGameHubConnectionFactory"/>), starts a new connection when a start fails or a connection
/// closes (a game restart can outlast both), and surfaces received messages as events.
/// </summary>
public sealed class ConnectionStateService : IConnectionStateService, ISceneHubControl, IAsyncDisposable
{
	private readonly IGameHubConnectionFactory _factory;
	private readonly ILogger<ConnectionStateService> _logger;

	private IGameHubConnection? _hub;
	// Phase 9: scene realtime now rides a SEPARATE connection to the plugin-owned hub at /hubs/scene
	// (ReceiveSceneMessage + JoinScene/LeaveScene), not the GameHub connection.
	private IGameHubConnection? _sceneHub;
	// A scene connection being started: a second caller waits for it rather than open another.
	private Task? _sceneStart;
	// The scene groups this client has joined, so a reconnect (which loses SignalR group membership) or a scene
	// connection opened after the join can join them again.
	private readonly HashSet<string> _joinedScenes = new(StringComparer.Ordinal);
	private SignalRState _innerState = SignalRState.Disconnected;
	private readonly List<IDisposable> _subscriptions = [];
	// A connection was asked for and not given up on purpose (DisconnectAsync). While it holds, a start that
	// fails or a connection that closes is tried again, so a game that restarts does not leave the portal
	// without a hub until someone happens to ask for one.
	private bool _wanted;
	private CancellationTokenSource? _retry;

	/// <summary>How long to wait before each new attempt after a start failed or the hub closed; the last delay
	/// repeats. Settable for tests.</summary>
	public IReadOnlyList<TimeSpan> RetryDelays { get; set; } =
		[TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

	public event Action? OnConnectionStateChanged;
	public event Action<GameOutputMessage>? OnOutputReceived;
	public event Action<RoomEventMessage>? OnRoomEventReceived;
	public event Action<SceneEventMessage>? OnSceneEventReceived;
	public event Action? OnPluginsChanged;
	public event Action? OnSceneLiveChanged;

	/// <inheritdoc/>
	public bool IsSceneLive => _sceneHub?.State == SignalRState.Connected;

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
	public Task ConnectAsync()
	{
		_wanted = true;
		return StartHubAsync();
	}

	private async Task StartHubAsync()
	{
		// Nobody wants a connection any more (DisconnectAsync ran while a retry was on its way here).
		if (!_wanted) return;
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
			// Not a DisconnectAsync (that clears _wanted first): the connection gave up on its own. Start a new
			// one, which the retry does after disposing this one outside its own callback.
			RetryLater();
			return Task.CompletedTask;
		};

		_hub.Reconnecting += ex =>
		{
			_logger.LogInformation(ex, "[ConnectionStateService] Hub reconnecting");
			SetState(SignalRState.Reconnecting);
			return Task.CompletedTask;
		};

		_hub.Reconnected += async _ =>
		{
			_logger.LogInformation("[ConnectionStateService] Hub reconnected");
			SetState(SignalRState.Connected);
			// The server is back. A scene connection still waiting out its own back-off would stay down until its
			// next attempt, up to 30 s on, with the story saying live poses are not arriving.
			await ReviveSceneHubAsync();
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
			RetryLater();
		}
		catch (HubException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (hub error)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
			RetryLater();
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (network)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
			RetryLater();
		}
		catch (TaskCanceledException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (cancelled)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
			RetryLater();
		}
		catch (OperationCanceledException ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed (operation cancelled)");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
			RetryLater();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] StartAsync failed with unexpected exception");
			SetState(SignalRState.Disconnected);
			await DisposeHubAsync();
			RetryLater();
		}
	}

	/// <inheritdoc/>
	public async Task DisconnectAsync()
	{
		_wanted = false;
		_retry?.Cancel();
		_retry = null;
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
	public async Task ReconnectAsync()
	{
		// A null hub means this session has never connected — the state EVERY caller arrives in the
		// first time. Character creation, terminal login and character switch all come through here,
		// and nothing calls ConnectAsync directly, so returning early left the game hub unconnected
		// for the whole session: no live scene poses, no room events, and a compose box on
		// /scenes/{id}/live that could never send. There is nothing to tear down first.
		if (_hub is null)
		{
			await ConnectAsync();
			return;
		}

		await DisconnectAsync();
		await ConnectAsync();
	}

	/// <summary>
	/// Starts the retry loop, unless one is running or nobody wants a connection: wait, then start a new hub,
	/// until one connects or <see cref="DisconnectAsync"/> says to stop.
	/// </summary>
	private void RetryLater()
	{
		if (!_wanted || _retry is not null) return;
		_retry = new CancellationTokenSource();
		_ = RetryAsync(_retry);
	}

	private async Task RetryAsync(CancellationTokenSource retry)
	{
		try
		{
			for (var attempt = 0; !retry.IsCancellationRequested; attempt++)
			{
				await Task.Delay(RetryDelays[Math.Min(attempt, RetryDelays.Count - 1)], retry.Token);
				if (!_wanted || IsConnected) return;
				// SignalR's own reconnect is still going: leave it be.
				if (_hub?.State is SignalRState.Reconnecting or SignalRState.Connecting) continue;
				// A hub that closed is spent; a new one replaces it.
				if (_hub is not null) await DisposeHubAsync();
				// DisconnectAsync may have run while the old hub was being disposed.
				if (retry.IsCancellationRequested || !_wanted) return;
				await StartHubAsync();
				if (IsConnected) return;
			}
		}
		catch (OperationCanceledException)
		{
			// DisconnectAsync stopped it.
		}
		finally
		{
			if (ReferenceEquals(_retry, retry)) _retry = null;
			retry.Dispose();
		}
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
		if (_sceneStart is { } pending)
		{
			await pending;
			return;
		}
		var start = StartSceneHubAsync();
		_sceneStart = start;
		try
		{
			await start;
		}
		finally
		{
			if (ReferenceEquals(_sceneStart, start)) _sceneStart = null;
		}
	}

	private async Task StartSceneHubAsync()
	{
		if (_sceneHub is not null) return;

		var sceneHub = _factory.CreateScene();
		if (sceneHub is null) return; // No scene hub configured — scene realtime simply stays inert.

		sceneHub.On("ReceiveSceneMessage", (SceneEventMessage msg) =>
		{
			_logger.LogDebug("[ConnectionStateService] ReceiveSceneMessage: {EventType}", msg.EventType);
			OnSceneEventReceived?.Invoke(msg);
		});
		sceneHub.Reconnecting += _ =>
		{
			OnSceneLiveChanged?.Invoke();
			return Task.CompletedTask;
		};
		// A reconnected connection has lost its groups: join them again before saying it is live.
		sceneHub.Reconnected += async _ =>
		{
			await RejoinScenesAsync();
			OnSceneLiveChanged?.Invoke();
		};
		sceneHub.Closed += _ =>
		{
			OnSceneLiveChanged?.Invoke();
			return Task.CompletedTask;
		};

		try
		{
			await sceneHub.StartAsync();
			_sceneHub = sceneHub;
			await RejoinScenesAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[ConnectionStateService] Scene hub StartAsync failed; scene realtime disabled");
			await sceneHub.DisposeAsync();
		}
		OnSceneLiveChanged?.Invoke();
	}

	/// <inheritdoc/>
	public async Task EnsureSceneLiveAsync()
	{
		if (IsSceneLive) return;
		if (!IsConnected)
		{
			// A connection SignalR is still bringing back, or one the retry loop is about to replace, is left
			// to them: stopping it here would end SignalR's reconnect for good.
			if (_hub?.State is SignalRState.Reconnecting or SignalRState.Connecting || _retry is not null) return;
			// Nothing is trying: open both connections (the scene one rides the game one's lifecycle).
			if (_hub is not null) await DisposeHubAsync();
			await ConnectAsync();
			return;
		}
		// The scene connection is coming back on its own.
		if (_sceneHub?.State is SignalRState.Reconnecting or SignalRState.Connecting) return;
		// The game connection is up but the scene one is not (it failed to start, or gave up reconnecting).
		await ReviveSceneHubAsync();
	}

	/// <summary>
	/// Replaces a scene connection that is not live with a new one, when the game connection says the server
	/// answers. One still reconnecting is replaced too: its next attempt is on SignalR's back-off, not now.
	/// </summary>
	private async Task ReviveSceneHubAsync()
	{
		if (IsSceneLive) return;
		if (_sceneHub is { } dead)
		{
			_sceneHub = null;
			await dead.DisposeAsync();
		}
		await ConnectSceneHubAsync();
	}

	/// <summary>Joins every scene this client asked for again, on a connection that has lost them.</summary>
	private async Task RejoinScenesAsync()
	{
		foreach (var sceneId in _joinedScenes.ToArray())
		{
			try
			{
				await InvokeSceneHubAsync("JoinScene", sceneId);
			}
			catch (HubException ex)
			{
				// The hub no longer lets this connection see the scene; the page that joined it says so on its
				// next join. Drop it rather than retry a refusal on every reconnect.
				_logger.LogDebug(ex, "[ConnectionStateService] Rejoining scene {SceneId} was refused", LogSanitizer.Sanitize(sceneId));
				_joinedScenes.Remove(sceneId);
			}
		}
	}

	/// <inheritdoc/>
	public async Task JoinSceneAsync(string sceneId)
	{
		await InvokeSceneHubAsync("JoinScene", sceneId);
		// Recorded after the call: a refusal throws, and a refused scene is not one to rejoin.
		_joinedScenes.Add(sceneId);
	}

	/// <inheritdoc/>
	public Task LeaveSceneAsync(string sceneId)
	{
		_joinedScenes.Remove(sceneId);
		return InvokeSceneHubAsync("LeaveScene", sceneId);
	}

	/// <summary>
	/// Invokes a scene-hub group method, treating an absent connection as the documented no-op.
	/// <para>The state check alone cannot deliver that: the scene connection can close between the check and
	/// the invoke, and SignalR then answers with a transport fault rather than a no-op —
	/// <see cref="InvalidOperationException"/> ("the connection is not active"),
	/// <see cref="ObjectDisposedException"/> for a connection torn down underneath the call, or a cancelled
	/// invocation when the close races an in-flight one. All three mean the same thing as the check that
	/// missed them by a hair, and the caller has nothing to do about any of them: the group membership dies
	/// with the connection. The window is not theoretical — <c>SceneLive</c> leaves its group from
	/// <c>DisposeAsync</c>, which is exactly when the connection is being taken down.</para>
	/// <para>A <see cref="HubException"/> is deliberately NOT caught here. That is not a transport fault but
	/// the hub's authorization answer (no character, or a scene the caller may not see), which the calling
	/// page renders.</para>
	/// </summary>
	private async Task InvokeSceneHubAsync(string method, string sceneId)
	{
		if (_sceneHub is not { } sceneHub || sceneHub.State != SignalRState.Connected) return;

		try
		{
			await sceneHub.InvokeAsync(method, sceneId);
		}
		catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
		{
			_logger.LogDebug(ex, "[ConnectionStateService] Scene hub {Method}({SceneId}) lost the connection mid-call",
				method, LogSanitizer.Sanitize(sceneId));
		}
	}

	private void SetState(SignalRState state)
	{
		_innerState = state;
		OnConnectionStateChanged?.Invoke();
	}

	private static LibraryState MapState(SignalRState inner) => inner switch
	{
		SignalRState.Disconnected => LibraryState.Disconnected,
		SignalRState.Connecting => LibraryState.Connecting,
		SignalRState.Connected => LibraryState.Connected,
		SignalRState.Reconnecting => LibraryState.Reconnecting,
		_ => LibraryState.Disconnected,
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
