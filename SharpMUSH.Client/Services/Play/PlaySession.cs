using System.Text.Json;
using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Client.Services.Play;

/// <summary>
/// The tab's play session: the play terminal's connection, its character and room, and the packages the server
/// sends that character, as one ordered stream of <see cref="PlayEvent"/>s. One per tab, built at startup so it
/// sees every package from the first. Applications get it through <see cref="ForApplication"/>, limited to the
/// packages they declare.
/// </summary>
/// <remarks>
/// The character and room come from the engine's <c>sharp.play</c> package; until one arrives the character is
/// the one the terminal connected as. A reset bumps <see cref="PlayState.Generation"/>, drops the cached packages
/// and is followed by a <see cref="PlayStateChanged"/>.
/// </remarks>
public sealed class PlaySession : IPlaySession, IAccountSessionEndingHandler, IDisposable
{
	/// <summary>The engine's own package: the character and room. Never delivered.</summary>
	public const string StatePackage = "sharp.play";

	private readonly TerminalServiceHost _terminal;
	private readonly Func<Task> _connect;
	private readonly Func<bool> _signedIn;
	private readonly ILogger<PlaySession> _logger;
	private readonly Lock _lock = new();
	private readonly List<PlaySubscriber> _subscribers = [];
	private readonly Dictionary<string, PlayPackage> _latest = new(StringComparer.Ordinal);

	private PlayState _state = PlayState.NotPlaying;
	private PlayCharacter? _announced;
	private string? _room;
	private bool _everConnected;
	private bool _ended;
	private bool _signedOut;

	public PlaySession(TerminalServiceHost terminal, Func<Task> connect, Func<bool> signedIn, ILogger<PlaySession> logger)
	{
		_terminal = terminal;
		_connect = connect;
		_signedIn = signedIn;
		_logger = logger;
		_terminal.ConnectionStateChanged += OnConnectionStateChanged;
		_terminal.Recreated += OnRecreated;
		_terminal.OobChannels.FrameReceived += OnFrame;
		_terminal.OobChannels.SessionSignaled += OnSessionSignal;
		_state = Compute(0);
	}

	/// <inheritdoc/>
	public PlayState State
	{
		get { lock (_lock) return _state; }
	}

	/// <inheritdoc/>
	public IDisposable Subscribe(PlaySubscription subscription, Func<PlayEvent, Task> handler) =>
		Subscribe(subscription, handler, scope: null, dispatch: null, owner: null);

	/// <summary>
	/// <see cref="Subscribe(PlaySubscription, Func{PlayEvent, Task})"/> limited to the packages
	/// <paramref name="scope"/> matches, each handler run through <paramref name="dispatch"/>.
	/// </summary>
	internal IDisposable Subscribe(PlaySubscription subscription, Func<PlayEvent, Task> handler,
		PackagePatterns? scope, Func<Func<Task>, Task>? dispatch, string? owner)
	{
		var subscriber = new PlaySubscriber(PackagePatterns.Of(subscription.Packages), scope, subscription.AcceptSelfSent,
			handler, dispatch, owner ?? "a subscriber", _logger);
		List<PlayEvent> cached = [];
		lock (_lock)
		{
			_subscribers.Add(subscriber);
			if (subscription.Delivery == PlayDelivery.Latest)
				cached.AddRange(_latest.Values
					.Where(subscriber.Accepts)
					.OrderBy(package => package.Package, StringComparer.Ordinal)
					.Select(package => package with { Origin = PlayOrigin.Cached }));
		}

		foreach (var item in cached) subscriber.Post(item);
		return new Unsubscriber(() =>
		{
			lock (_lock) _subscribers.Remove(subscriber);
			subscriber.Stop();
		});
	}

	/// <inheritdoc/>
	public async Task ConnectAsync()
	{
		if (_terminal.IsConnected) return;
		await _connect();
	}

	/// <summary>
	/// The session as <paramref name="application"/> sees it: only the packages it declares in
	/// <paramref name="declared"/>, each handler run through <paramref name="dispatch"/>. Disposing it ends every
	/// subscription made through it.
	/// </summary>
	public ApplicationPlaySession ForApplication(string application, IReadOnlyList<string> declared,
		Func<Func<Task>, Task>? dispatch = null) =>
		new(this, application, PackagePatterns.Of(declared), dispatch);

	/// <summary>Sign-out: everything held for this account is stale before the terminals hang up.</summary>
	public Task OnAccountSessionEndingAsync()
	{
		lock (_lock) _signedOut = true;
		Reset(PlayResetReason.SignedOut);
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_terminal.ConnectionStateChanged -= OnConnectionStateChanged;
		_terminal.Recreated -= OnRecreated;
		_terminal.OobChannels.FrameReceived -= OnFrame;
		_terminal.OobChannels.SessionSignaled -= OnSessionSignal;
		PlaySubscriber[] subscribers;
		lock (_lock)
		{
			subscribers = [.. _subscribers];
			_subscribers.Clear();
		}

		foreach (var subscriber in subscribers) subscriber.Stop();
	}

	private void OnConnectionStateChanged(bool connected)
	{
		lock (_lock)
		{
			if (connected)
			{
				_everConnected = true;
				_ended = false;
				_signedOut = false;
			}
		}

		Refresh();
	}

	private void OnRecreated()
	{
		bool signedOut;
		lock (_lock) signedOut = _signedOut;
		// A sign-out already reset; the rebuild that follows it is the same change.
		if (!signedOut) Reset(_signedIn() ? PlayResetReason.CharacterChanged : PlayResetReason.SignedOut);
	}

	private void OnSessionSignal(OobSessionSignal signal)
	{
		switch (signal)
		{
			case OobSessionSignal.ResumeRefused:
				Reset(PlayResetReason.NewSession);
				return;
			case OobSessionSignal.Ended:
				bool already;
				lock (_lock)
				{
					already = _ended || _signedOut;
					_ended = true;
				}

				if (!already) Reset(PlayResetReason.Ended);
				return;
		}
	}

	private void OnFrame(OobFrame frame)
	{
		// The state goes first, so an application that also reads sharp.play sees the package after the reset and
		// state change it brings.
		if (frame.Package == StatePackage) OnStateFrame(frame.DataJson);

		if (PackagePatterns.IsReserved(frame.Package)) return;

		PlayPackage package;
		PlaySubscriber[] subscribers;
		lock (_lock)
		{
			var data = ParseOrNull(frame.DataJson);
			var sender = frame.From switch
			{
				null => PlaySender.Unknown,
				var from when from == _state.Character?.ObjId => PlaySender.Self,
				_ => PlaySender.Other
			};
			package = new PlayPackage(_state.Generation, frame.Package, PlayPackage.VersionOf(data), data,
				frame.CatchUp ? PlayOrigin.CatchUp : PlayOrigin.Live, sender, frame.From);
			_latest[frame.Package] = package;
			subscribers = [.. _subscribers];
		}

		foreach (var subscriber in subscribers.Where(subscriber => subscriber.Accepts(package)))
			subscriber.Post(package);
	}

	/// <summary>
	/// The engine's <c>sharp.play</c>: <c>{"v":1,"character":"#12:…","name":"…","room":"#0:…"}</c>, or
	/// <c>{"v":1,"character":null}</c> when the connection logged out and stays open.
	/// </summary>
	private void OnStateFrame(string dataJson)
	{
		var data = ParseOrNull(dataJson);
		if (data.ValueKind != JsonValueKind.Object || PlayPackage.VersionOf(data) != 1) return;

		var character = StringOf(data, "character");
		// No character: the connection logged out (LOGOUT) and is back at the login screen, still open.
		if (character is null)
		{
			OnSessionSignal(OobSessionSignal.Ended);
			return;
		}

		PlayCharacter? previous;
		lock (_lock)
		{
			previous = _ended ? null : _state.Character;
			// Logged in again on the same open connection.
			_ended = false;
		}

		var announced = new PlayCharacter(character, StringOf(data, "name") ?? string.Empty, _terminal.IsGuest);
		var room = StringOf(data, "room");
		// The same connection now plays someone else: what was held for the previous character goes, and the state
		// after the reset already names the new one.
		if (previous is not null && previous.ObjId != character)
		{
			Reset(PlayResetReason.CharacterChanged, (announced, room));
			return;
		}

		lock (_lock)
		{
			_room = room;
			_announced = announced;
		}

		Refresh();
	}

	/// <summary>Starts a new generation: drops what the previous one held and tells every subscriber.</summary>
	/// <param name="announced">The character, and the room it is in, that the reset is for, when the engine said.</param>
	private void Reset(PlayResetReason reason, (PlayCharacter Character, string? Room)? announced = null)
	{
		PlayState previous, current;
		PlaySubscriber[] subscribers;
		lock (_lock)
		{
			previous = _state;
			_latest.Clear();
			_room = announced?.Room;
			_everConnected = _terminal.IsConnected;
			// A refused resume logs the same character in again; anything else may be someone else.
			if (announced is { } next) _announced = next.Character;
			else if (reason != PlayResetReason.NewSession) _announced = null;
			current = _state = Compute(previous.Generation + 1);
			subscribers = [.. _subscribers];
		}

		var reset = new PlayReset(current.Generation, reason);
		var changed = new PlayStateChanged(current.Generation, previous, current);
		foreach (var subscriber in subscribers)
		{
			subscriber.Post(reset);
			subscriber.Post(changed);
		}
	}

	/// <summary>Recomputes the state and tells every subscriber when it changed.</summary>
	private void Refresh()
	{
		PlayState previous, current;
		PlaySubscriber[] subscribers;
		lock (_lock)
		{
			previous = _state;
			current = Compute(previous.Generation);
			if (current == previous) return;
			_state = current;
			subscribers = [.. _subscribers];
		}

		var changed = new PlayStateChanged(current.Generation, previous, current);
		foreach (var subscriber in subscribers) subscriber.Post(changed);
	}

	/// <summary>The state as the terminal and the last <c>sharp.play</c> describe it. Called under the lock.</summary>
	private PlayState Compute(long generation)
	{
		if (_ended || _signedOut) return PlayState.NotPlaying with { Generation = generation };

		var connected = _terminal.IsConnected;
		var identity = _terminal.Identity;
		var character = _announced
			?? (identity is { } id
				? new PlayCharacter(id.Character, _terminal.ConnectedPlayerName ?? string.Empty, _terminal.IsGuest)
				: null);
		var connection = connected ? PlayConnection.Connected
			: identity is null ? PlayConnection.NotPlaying
			: _everConnected ? PlayConnection.Reconnecting
			: PlayConnection.Connecting;
		if (connection == PlayConnection.NotPlaying) character = null;
		return new PlayState(character, connection, character is null ? null : _room, generation);
	}

	private static JsonElement ParseOrNull(string json)
	{
		if (string.IsNullOrWhiteSpace(json)) return default;
		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return default;
		}
	}

	private static string? StringOf(JsonElement data, string name) =>
		data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
			? text
			: null;

	private sealed class Unsubscriber(Action dispose) : IDisposable
	{
		private Action? _dispose = dispose;

		public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
	}
}
