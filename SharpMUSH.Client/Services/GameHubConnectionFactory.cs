using Microsoft.AspNetCore.SignalR.Client;
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
	/// Creates and returns a new hub connection configured with the supplied
	/// <paramref name="accessToken"/>.  The caller is responsible for starting
	/// and disposing the returned connection.
	/// </summary>
	IGameHubConnection Create(string accessToken);
}

/// <summary>
/// Production implementation — builds a real <see cref="HubConnection"/> pointed
/// at <c>/hubs/game</c> and wraps it in <see cref="RealGameHubConnection"/>.
/// </summary>
public sealed class GameHubConnectionFactory : IGameHubConnectionFactory
{
	private readonly string _hubUrl;

	/// <param name="hubUrl">Absolute URL of the hub endpoint, e.g. "https://host/hubs/game".</param>
	public GameHubConnectionFactory(string hubUrl) => _hubUrl = hubUrl;

	/// <inheritdoc/>
	public IGameHubConnection Create(string accessToken)
	{
		var connection = new HubConnectionBuilder()
			.WithUrl(_hubUrl, opts =>
			{
				opts.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
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
