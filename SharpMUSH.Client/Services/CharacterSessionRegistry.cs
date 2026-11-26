using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The tab's character sessions. <c>/play</c> uses one, <see cref="Focused"/>: the character the tab acts as.
/// <c>/client</c> keeps one for each of the account's characters the player opens, each connected, and the tab
/// acts as whichever is focused.
/// </summary>
/// <remarks>It assumes a single-threaded dispatcher (Blazor WASM).</remarks>
public sealed class CharacterSessionRegistry
{
	/// <summary>How many characters one tab keeps connected at most.</summary>
	public const int Limit = 8;

	private readonly Func<CharacterSession>? _open;
	private readonly List<CharacterSession> _sessions = [];

	/// <summary>Each session's line handler, so closing it can take the handler off again.</summary>
	private readonly Dictionary<CharacterSession, Action<TerminalLine>> _heard = [];

	/// <param name="focused">The tab's first session.</param>
	/// <param name="open">Builds a session over a new connection; without it the tab keeps only the first.</param>
	public CharacterSessionRegistry(CharacterSession focused, Func<CharacterSession>? open = null)
	{
		_open = open;
		Focused = focused;
		Track(focused);
	}

	/// <summary>The session the tab acts as, and the one Play shows.</summary>
	public CharacterSession Focused { get; private set; }

	/// <summary>Every session in the tab, in the order they were opened.</summary>
	public IReadOnlyList<CharacterSession> Sessions => _sessions;

	/// <summary>
	/// Whether a character switch opens a session for the character and keeps the others connected, as on
	/// <c>/client</c>, rather than moving the focused session's connection to it, as on <c>/play</c>. Once on it
	/// stays on for the tab, so leaving <c>/client</c> for another page drops no one.
	/// </summary>
	public bool KeepsSessions { get; set; }

	/// <summary>Whether another session can be opened.</summary>
	public bool CanOpen => _open is not null && _sessions.Count < Limit;

	/// <summary>Raised when <see cref="Focused"/> changes.</summary>
	public event Action? FocusChanged;

	/// <summary>Raised when a session opens or closes, the focus moves, or a session in the background hears something.</summary>
	public event Action? Changed;

	/// <summary>The session playing <paramref name="identity"/>.</summary>
	public Found<CharacterSession> For(TerminalIdentity identity) =>
		_sessions.FirstOrDefault(session => session.Identity == identity) is { } found ? found : new NotFound();

	/// <summary>A session over a new connection, not yet connected nor focused.</summary>
	public CharacterSession Open()
	{
		if (_open is null) throw new InvalidOperationException("This tab keeps one character session.");
		if (_sessions.Count >= Limit) throw new InvalidOperationException($"A tab keeps at most {Limit} characters.");

		var session = _open();
		Track(session);
		Changed?.Invoke();
		return session;
	}

	/// <summary>Brings <paramref name="session"/> to the front: the tab acts as it and Play shows it.</summary>
	public void Focus(CharacterSession session)
	{
		if (!_sessions.Contains(session)) throw new ArgumentException("The session is not this tab's.", nameof(session));
		if (ReferenceEquals(session, Focused)) return;

		Focused = session;
		session.Seen();
		FocusChanged?.Invoke();
		Changed?.Invoke();
	}

	/// <summary>
	/// Logs <paramref name="session"/>'s character out (<c>QUIT</c>, so it leaves the WHO list at once) and lets it go.
	/// The focused session stays: focus another first.
	/// </summary>
	public async Task CloseAsync(CharacterSession session)
	{
		if (ReferenceEquals(session, Focused)) throw new InvalidOperationException("Focus another session before closing this one.");
		if (!_sessions.Remove(session)) return;

		if (_heard.Remove(session, out var heard)) session.Terminal.LineReceived -= heard;
		Changed?.Invoke();

		if (session.Terminal.IsConnected)
		{
			await session.Terminal.SendAsync("QUIT");
			await session.Terminal.DisconnectAsync();
		}

		await session.DisposeAsync();
	}

	/// <summary>Counts what a session hears while another is in front.</summary>
	private void Track(CharacterSession session)
	{
		Action<TerminalLine> heard = _ =>
		{
			if (ReferenceEquals(session, Focused)) return;
			session.Heard();
			Changed?.Invoke();
		};
		_heard[session] = heard;
		_sessions.Add(session);
		session.Terminal.LineReceived += heard;
	}
}
