using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The play terminal of the character the tab acts as: <see cref="CharacterSessionRegistry.Focused"/>'s, followed
/// as the focus moves. What every <c>@inject IPlayTerminalService</c> gets, so the Room panel, the palette and the
/// rail read the character in front without knowing there are others.
/// </summary>
/// <remarks>
/// A move to another session says so the way a rebuilt connection does: the connection state and the prompt of the
/// new one, every out-of-band package either held (<see cref="OobChannelStoreProxy.Follow"/>), and
/// <see cref="Recreated"/>. The session left behind keeps its connection.
/// </remarks>
public sealed class FocusedPlayTerminal : IPlayTerminalService, ITerminalFacade
{
	private readonly CharacterSessionRegistry _sessions;
	private readonly OobChannelStoreProxy _oob = new();
	private IPlayTerminalService _target;

	public FocusedPlayTerminal(CharacterSessionRegistry sessions)
	{
		_sessions = sessions;
		_target = sessions.Focused.Terminal;
		Attach(_target);
		_oob.Follow(_target.OobChannels);
		sessions.FocusChanged += OnFocusChanged;
	}

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;
	public event Action? PromptChanged;
	public event Action? Recreated;

	private void OnFocusChanged()
	{
		var next = _sessions.Focused.Terminal;
		if (ReferenceEquals(next, _target)) return;

		Detach(_target);
		_target = next;
		Attach(next);
		_oob.Follow(next.OobChannels);
		ConnectionStateChanged?.Invoke(next.IsConnected);
		PromptChanged?.Invoke();
		Recreated?.Invoke();
	}

	private void Attach(IPlayTerminalService target)
	{
		target.LineReceived += OnLine;
		target.ConnectionStateChanged += OnConnectionState;
		target.PromptChanged += OnPromptChanged;
		if (target is ITerminalFacade facade) facade.Recreated += OnRecreated;
	}

	private void Detach(IPlayTerminalService target)
	{
		target.LineReceived -= OnLine;
		target.ConnectionStateChanged -= OnConnectionState;
		target.PromptChanged -= OnPromptChanged;
		if (target is ITerminalFacade facade) facade.Recreated -= OnRecreated;
	}

	private void OnLine(TerminalLine line) => LineReceived?.Invoke(line);
	private void OnConnectionState(bool connected) => ConnectionStateChanged?.Invoke(connected);
	private void OnPromptChanged() => PromptChanged?.Invoke();
	private void OnRecreated() => Recreated?.Invoke();

	public bool IsConnected => _target.IsConnected;

	public string? ConnectedPlayerName
	{
		get => _target.ConnectedPlayerName;
		set => _target.ConnectedPlayerName = value;
	}

	public string? ServerUri => _target.ServerUri;
	public TerminalIdentity? Identity => _target.Identity;
	public bool IsGuest => _target.IsGuest;
	public IReadOnlyList<TerminalLine> Lines => _target.Lines;
	public TerminalPrompt? Prompt => _target.Prompt;
	public IOobChannelStore OobChannels => _oob;
	public TerminalServiceHost? DraftKeeper => (_target as ITerminalFacade)?.DraftKeeper;

	public Task ConnectAsync(string serverUri) => _target.ConnectAsync(serverUri);
	public Task ConnectWithOttAsync(string serverUri, string ott, TerminalIdentity? identity = null)
		=> _target.ConnectWithOttAsync(serverUri, ott, identity);
	public Task ConnectAsGuestAsync(string serverUri) => _target.ConnectAsGuestAsync(serverUri);
	public Task DisconnectAsync() => _target.DisconnectAsync();
	public Task SendAsync(string command) => _target.SendAsync(command);
	public Task SendControlAsync(string controlJson) => _target.SendControlAsync(controlJson);
	public void MarkEarlier(IReadOnlyList<TerminalLine> lines) => _target.MarkEarlier(lines);
	public Task<string[]> SendCommandAsync(string expression, int timeoutMs = 5000)
		=> _target.SendCommandAsync(expression, timeoutMs);

	/// <summary>Lets go of the sessions; their connections belong to them, not to this.</summary>
	public ValueTask DisposeAsync()
	{
		_sessions.FocusChanged -= OnFocusChanged;
		Detach(_target);
		return ValueTask.CompletedTask;
	}
}

/// <summary>
/// The channels and pages of the character the tab acts as, followed as the focus moves; raises
/// <see cref="Changed"/> on a move, so a reader shows the new character's.
/// </summary>
public sealed class FocusedCommFeed : ICommFeed
{
	private readonly CharacterSessionRegistry _sessions;
	private ICommFeed _target;

	public FocusedCommFeed(CharacterSessionRegistry sessions)
	{
		_sessions = sessions;
		_target = sessions.Focused.Comms;
		_target.Changed += OnChanged;
		sessions.FocusChanged += OnFocusChanged;
	}

	public event Action? Changed;

	private void OnFocusChanged()
	{
		var next = _sessions.Focused.Comms;
		if (ReferenceEquals(next, _target)) return;

		// The character left behind is no longer looked at: what it hears from now on is unread. Its view closes
		// after this, through this facade, so it could not clear its own keys.
		_target.Viewed = [];
		_target.Changed -= OnChanged;
		_target = next;
		_target.Changed += OnChanged;
		Changed?.Invoke();
	}

	private void OnChanged() => Changed?.Invoke();

	public IReadOnlyList<CommChannel> Channels => _target.Channels;
	public IReadOnlyList<CommConversation> Conversations => _target.Conversations;
	public IReadOnlyList<CommMessage> Messages(string key) => _target.Messages(key);
	public void MarkRead(string key) => _target.MarkRead(key);
	public Task<bool> LoadHistoryAsync(string key) => _target.LoadHistoryAsync(key);
	public bool? PageLogging => _target.PageLogging;

	public string? Viewing
	{
		get => _target.Viewing;
		set => _target.Viewing = value;
	}

	public IReadOnlyList<string> Viewed
	{
		get => _target.Viewed;
		set => _target.Viewed = value;
	}

	public ConversationOpening Converse(string name, string objId) => _target.Converse(name, objId);
}

/// <summary>The channel member lists of the character the tab acts as, followed as the focus moves.</summary>
public sealed class FocusedChannelWho : IChannelWho
{
	private readonly CharacterSessionRegistry _sessions;
	private IChannelWho _target;

	public FocusedChannelWho(CharacterSessionRegistry sessions)
	{
		_sessions = sessions;
		_target = sessions.Focused.ChannelWho;
		_target.Changed += OnChanged;
		sessions.FocusChanged += OnFocusChanged;
	}

	public event Action? Changed;

	private void OnFocusChanged()
	{
		var next = _sessions.Focused.ChannelWho;
		if (ReferenceEquals(next, _target)) return;

		_target.Changed -= OnChanged;
		_target = next;
		_target.Changed += OnChanged;
		Changed?.Invoke();
	}

	private void OnChanged() => Changed?.Invoke();

	public IReadOnlyList<CommParticipant>? Members(string channel) => _target.Members(channel);
	public Task WatchAsync(string channel) => _target.WatchAsync(channel);
	public void Unwatch(string channel) => _target.Unwatch(channel);
}
