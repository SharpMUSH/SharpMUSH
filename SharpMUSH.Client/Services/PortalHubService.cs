using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side service wrapping HubConnection for the Portal SignalR hub.
/// Handles auto-connect on auth, auto-reconnect on disconnect with backoff.
/// </summary>
public class PortalHubService : IAsyncDisposable
{
	private readonly NavigationManager _navigationManager;
	private readonly ILogger<PortalHubService> _logger;
	private HubConnection? _connection;
	private CancellationTokenSource? _reconnectCts;

	public HubConnection? Connection => _connection;
	public bool IsConnected => _connection?.State == HubConnectionState.Connected;

	public event Func<string, string, string, Task>? PoseReceived;
	public event Func<string, string, string, Task>? PresenceChanged;
	public event Func<string, string, long, Task>? WikiPageUpdated;
	public event Func<string, string, string, string, Task>? MailReceived;
	public event Func<string, string, Task>? NotificationReceived;
	public event Func<Task>? Connected;
	public event Func<Exception?, Task>? Disconnected;

	public PortalHubService(NavigationManager navigationManager, ILogger<PortalHubService> logger)
	{
		_navigationManager = navigationManager;
		_logger = logger;
	}

	/// <summary>
	/// Establishes connection to the Portal hub with JWT token.
	/// Automatically sets up event handlers and reconnection logic.
	/// </summary>
	public async Task ConnectAsync(string jwtToken)
	{
		if (_connection != null)
		{
			_logger.LogWarning("[PortalHubService] Already connected, skipping ConnectAsync");
			return;
		}

		try
		{
			var baseUrl = _navigationManager.BaseUri;
			var hubUrl = new Uri(new Uri(baseUrl), "hubs/portal").ToString();

			_logger.LogInformation("[PortalHubService] Connecting to {HubUrl}", hubUrl);

			_connection = new HubConnectionBuilder()
				.WithUrl(hubUrl, options =>
				{
					options.AccessTokenProvider = async () => jwtToken;
					options.SkipNegotiation = false;
					options.Transport = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSocketsAndServerSentEvents;
				})
				.WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
				.AddJsonProtocol()
				.WithConsoleLogger(Microsoft.AspNetCore.SignalR.Client.Core.LogLevel.Information)
				.Build();

			// Register event handlers
			_connection.On<string, string, string>("OnPoseReceived",
				async (sceneId, characterName, poseText) =>
				{
					_logger.LogDebug("[PortalHubService] Pose received from {Character} in {Scene}", characterName, sceneId);
					if (PoseReceived != null)
						await PoseReceived.Invoke(sceneId, characterName, poseText);
				});

			_connection.On<string, string, string>("OnPresenceChanged",
				async (sceneId, characterName, action) =>
				{
					_logger.LogDebug("[PortalHubService] Presence changed: {Character} {Action} {Scene}", characterName, action, sceneId);
					if (PresenceChanged != null)
						await PresenceChanged.Invoke(sceneId, characterName, action);
				});

			_connection.On<string, string, long>("OnWikiPageUpdated",
				async (pageName, updatedBy, timestamp) =>
				{
					_logger.LogDebug("[PortalHubService] Wiki updated: {Page} by {UpdatedBy}", pageName, updatedBy);
					if (WikiPageUpdated != null)
						await WikiPageUpdated.Invoke(pageName, updatedBy, timestamp);
				});

			_connection.On<string, string, string, string>("OnMailReceived",
				async (mailId, fromCharacter, subject, timestamp) =>
				{
					_logger.LogDebug("[PortalHubService] Mail received from {FromChar}: {Subject}", fromCharacter, subject);
					if (MailReceived != null)
						await MailReceived.Invoke(mailId, fromCharacter, subject, timestamp);
				});

			_connection.On<string, string>("OnNotification",
				async (message, notificationType) =>
				{
					_logger.LogInformation("[PortalHubService] Notification ({Type}): {Message}", notificationType, message);
					if (NotificationReceived != null)
						await NotificationReceived.Invoke(message, notificationType);
				});

			_connection.Reconnected += async () =>
			{
				_logger.LogInformation("[PortalHubService] Reconnected to hub");
				if (Connected != null)
					await Connected.Invoke();
			};

			_connection.Closed += async (ex) =>
			{
				_logger.LogWarning(ex, "[PortalHubService] Hub connection closed");
				if (Disconnected != null)
					await Disconnected.Invoke(ex);
			};

			await _connection.StartAsync();

			_logger.LogInformation("[PortalHubService] Connected to Portal hub");
			if (Connected != null)
				await Connected.Invoke();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[PortalHubService] Failed to connect to Portal hub");
			throw;
		}
	}

	/// <summary>
	/// Joins a scene to receive updates for that scene.
	/// </summary>
	public async Task JoinSceneAsync(string sceneId)
	{
		if (!IsConnected)
		{
			_logger.LogWarning("[PortalHubService] Not connected, cannot join scene");
			return;
		}

		try
		{
			await _connection!.InvokeAsync("JoinScene", sceneId);
			_logger.LogInformation("[PortalHubService] Joined scene {SceneId}", sceneId);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[PortalHubService] Failed to join scene {SceneId}", sceneId);
			throw;
		}
	}

	/// <summary>
	/// Leaves a scene to stop receiving updates for that scene.
	/// </summary>
	public async Task LeaveSceneAsync(string sceneId)
	{
		if (!IsConnected)
		{
			_logger.LogWarning("[PortalHubService] Not connected, cannot leave scene");
			return;
		}

		try
		{
			await _connection!.InvokeAsync("LeaveScene", sceneId);
			_logger.LogInformation("[PortalHubService] Left scene {SceneId}", sceneId);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[PortalHubService] Failed to leave scene {SceneId}", sceneId);
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_connection != null)
		{
			await _connection.DisposeAsync();
		}

		_reconnectCts?.Dispose();
	}

	/// <summary>
	/// Exponential backoff retry policy for automatic reconnection.
	/// </summary>
	private class ExponentialBackoffRetryPolicy : IRetryPolicy
	{
		public TimeSpan? NextRetryDelay(RetryContext context)
		{
			var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, context.PreviousRetryCount), 60));
			return delay;
		}
	}
}
