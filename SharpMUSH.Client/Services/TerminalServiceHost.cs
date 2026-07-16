using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A stable <see cref="ITerminalService"/> wrapping a swappable inner terminal, so the connection
/// can be torn down and rebuilt without any consumer re-resolving the service.
/// </summary>
/// <remarks>
/// A plain DI swap is not possible: every <c>@inject</c> resolves once at component init and holds
/// the reference for the component's lifetime, and <see cref="MushQueryService"/> constructor-captures
/// it for the app's lifetime with no re-injection path. Replacing the singleton would leave all of
/// them pointed at a dead instance. This facade keeps one object identity forever and moves the
/// churn behind it.
/// </remarks>
public class TerminalServiceHost : ITerminalService
{
	private readonly Func<ITerminalService> _factory;
	private readonly OobChannelStoreProxy _oob = new();
	private ITerminalService _inner;

	public TerminalServiceHost(Func<ITerminalService> factory)
	{
		_factory = factory;
		_inner = _factory();
		Attach(_inner);
	}

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;

	/// <summary>
	/// Disposes the current inner terminal and builds a fresh one. Subscribers keep their
	/// subscriptions to this facade and are re-pointed transparently.
	/// </summary>
	public async Task RecreateAsync()
	{
		Detach(_inner);
		await _inner.DisposeAsync();

		_inner = _factory();
		Attach(_inner);
	}

	private void Attach(ITerminalService inner)
	{
		inner.LineReceived += OnLine;
		inner.ConnectionStateChanged += OnConnectionState;
		_oob.SetInner(inner.OobChannels);
	}

	private void Detach(ITerminalService inner)
	{
		inner.LineReceived -= OnLine;
		inner.ConnectionStateChanged -= OnConnectionState;
	}

	private void OnLine(TerminalLine line) => LineReceived?.Invoke(line);
	private void OnConnectionState(bool connected) => ConnectionStateChanged?.Invoke(connected);

	public bool IsConnected => _inner.IsConnected;

	public string? ConnectedPlayerName
	{
		get => _inner.ConnectedPlayerName;
		set => _inner.ConnectedPlayerName = value;
	}

	public string? ServerUri => _inner.ServerUri;
	public long? MyPort => _inner.MyPort;
	public IReadOnlyList<TerminalLine> Lines => _inner.Lines;
	public IOobChannelStore OobChannels => _oob;

	public Task ConnectAsync(string serverUri) => _inner.ConnectAsync(serverUri);

	public Task ConnectAndLoginAsync(string serverUri, string playerName, string password, OttAuthService ottAuth)
		=> _inner.ConnectAndLoginAsync(serverUri, playerName, password, ottAuth);

	public Task ConnectWithOttAsync(string serverUri, string ott) => _inner.ConnectWithOttAsync(serverUri, ott);
	public Task DisconnectAsync() => _inner.DisconnectAsync();
	public Task SendAsync(string command) => _inner.SendAsync(command);
	public Task SendControlAsync(string controlJson) => _inner.SendControlAsync(controlJson);
	public Task InitializePortAsync() => _inner.InitializePortAsync();
	public Task<string[]> SendCommandAsync(string command, int timeoutMs = 5000)
		=> _inner.SendCommandAsync(command, timeoutMs);

	public async ValueTask DisposeAsync()
	{
		Detach(_inner);
		await _inner.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}

/// <summary>The play-terminal facade. Same swap mechanics, distinct DI identity.</summary>
public sealed class PlayTerminalServiceHost(Func<IPlayTerminalService> factory)
	: TerminalServiceHost(factory), IPlayTerminalService;
