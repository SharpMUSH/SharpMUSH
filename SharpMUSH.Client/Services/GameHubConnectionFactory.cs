using Microsoft.AspNetCore.SignalR.Client;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Factory interface so <see cref="ConnectionStateService"/> can create a new
/// <see cref="IGameHubConnection"/> per connect call.  Kept separate so tests
/// can inject a factory that returns mocks.
/// </summary>
public interface IGameHubConnectionFactory
{
	/// <summary>
	/// Creates and returns a new hub connection authenticated with the current
	/// account-session token (re-read on every connect/reconnect attempt — see
	/// <see cref="GameHubConnectionFactory.ResolveAccessTokenAsync"/>).  The caller
	/// is responsible for starting and disposing the returned connection.
	/// </summary>
	IGameHubConnection Create();

	/// <summary>
	/// Creates a connection to the plugin-owned scene realtime hub (<c>/hubs/scene</c>), where the
	/// Scene plugin's <c>SceneHub</c> is mapped (Phase 9). Returns <c>null</c> when no scene hub URL is
	/// configured. The caller starts and disposes the returned connection.
	/// </summary>
	IGameHubConnection? CreateScene();
}

/// <summary>
/// Production implementation — builds a real <see cref="HubConnection"/> pointed
/// at <c>/hubs/game</c> and wraps it in <see cref="RealGameHubConnection"/>.
/// </summary>
public sealed class GameHubConnectionFactory : IGameHubConnectionFactory
{
	private readonly string _hubUrl;
	private readonly string? _sceneHubUrl;
	private readonly IAccountAuthState _accountAuth;

	/// <param name="hubUrl">Absolute URL of the game hub endpoint, e.g. "https://host/hubs/game".</param>
	/// <param name="accountAuth">
	/// Live source of the account-session token. Held (not copied) so <see cref="ResolveAccessTokenAsync"/>
	/// always reflects the current session — including a login/logout that happens after this factory
	/// was constructed.
	/// </param>
	/// <param name="sceneHubUrl">Absolute URL of the scene hub endpoint, e.g. "https://host/hubs/scene".</param>
	public GameHubConnectionFactory(string hubUrl, IAccountAuthState accountAuth, string? sceneHubUrl = null)
	{
		_hubUrl = hubUrl;
		_accountAuth = accountAuth;
		_sceneHubUrl = sceneHubUrl;
	}

	/// <summary>
	/// Resolves the token SignalR's <c>AccessTokenProvider</c> hands to the hub on every (re)connect
	/// attempt. Deliberately reads <see cref="IAccountAuthState.AccountSessionToken"/> live on each
	/// call rather than closing over a value captured once at build time: <c>WithAutomaticReconnect</c>
	/// re-invokes this delegate on every reconnect attempt against the same long-lived
	/// <see cref="HubConnection"/>, so a snapshot would keep offering a stale (or since-cleared) token
	/// after a logout/re-login in the same tab. Public (not private) so tests can call it directly
	/// without reaching into <see cref="HubConnection"/>'s internals, which do not expose the
	/// configured provider for inspection.
	/// </summary>
	public Task<string?> ResolveAccessTokenAsync() => Task.FromResult(_accountAuth.AccountSessionToken);

	/// <summary>
	/// Appends the active character as a <c>character</c> query param (numeric key — '#' is a URL
	/// fragment delimiter) so the server binds this connection to the switched-to character. Read at
	/// build time: switching reconnects, so a fresh connection picks up the new character.
	/// </summary>
	public string ResolveHubUrl(string baseUrl)
	{
		if (_accountAuth.ActiveCharacter is not { } character)
			return baseUrl;

		var separator = baseUrl.Contains('?') ? '&' : '?';
		return $"{baseUrl}{separator}character={character.DbrefNumber}";
	}

	/// <inheritdoc/>
	public IGameHubConnection Create() => Build(_hubUrl);

	/// <inheritdoc/>
	public IGameHubConnection? CreateScene() =>
		_sceneHubUrl is null ? null : Build(_sceneHubUrl);

	private IGameHubConnection Build(string url)
	{
		var connection = new HubConnectionBuilder()
			.WithUrl(ResolveHubUrl(url), opts =>
			{
				opts.AccessTokenProvider = ResolveAccessTokenAsync;
			})
			.WithAutomaticReconnect(new ExponentialBackOffRetryPolicy())
			.Build();

		return new RealGameHubConnection(connection);
	}
}

/// <summary>
/// Exponential back-off policy: 1 s → 2 s → 4 s → 8 s → 30 s cap.
/// </summary>
internal sealed class ExponentialBackOffRetryPolicy : IRetryPolicy
{
	private static readonly TimeSpan[] Delays = [
		TimeSpan.FromSeconds(1),
		TimeSpan.FromSeconds(2),
		TimeSpan.FromSeconds(4),
		TimeSpan.FromSeconds(8),
		TimeSpan.FromSeconds(30),
	];

	public TimeSpan? NextRetryDelay(RetryContext retryContext)
	{
		var idx = (int)Math.Min(retryContext.PreviousRetryCount, Delays.Length - 1);
		return Delays[idx];
	}
}

/// <summary>
/// Wraps a real <see cref="HubConnection"/> and adapts it to
/// <see cref="IGameHubConnection"/>.
/// </summary>
internal sealed class RealGameHubConnection(HubConnection inner) : IGameHubConnection
{
	public HubConnectionState State => inner.State;

	public Task StartAsync(CancellationToken cancellationToken = default)
		=> inner.StartAsync(cancellationToken);

	public Task StopAsync(CancellationToken cancellationToken = default)
		=> inner.StopAsync(cancellationToken);

	public Task InvokeAsync(string methodName, string arg, CancellationToken cancellationToken = default)
		=> inner.InvokeAsync(methodName, arg, cancellationToken);

	public IDisposable On(string methodName, Action<GameOutputMessage> handler)
		=> inner.On(methodName, handler);

	public IDisposable On(string methodName, Action<RoomEventMessage> handler)
		=> inner.On(methodName, handler);

	public IDisposable On(string methodName, Action<SceneEventMessage> handler)
		=> inner.On(methodName, handler);

	public IDisposable On(string methodName, Action handler)
		=> inner.On(methodName, handler);

	public event Func<Exception?, Task>? Closed
	{
		add => inner.Closed += value;
		remove => inner.Closed -= value;
	}

	public event Func<Exception?, Task>? Reconnecting
	{
		add => inner.Reconnecting += value;
		remove => inner.Reconnecting -= value;
	}

	public event Func<string?, Task>? Reconnected
	{
		add => inner.Reconnected += value;
		remove => inner.Reconnected -= value;
	}

	public ValueTask DisposeAsync() => inner.DisposeAsync();
}
