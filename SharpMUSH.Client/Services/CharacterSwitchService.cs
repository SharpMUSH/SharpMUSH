using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Switches the portal's acting character. The switch is a server-side rebind: the endpoint mints a
/// session token bound to the target and this tab adopts it, so every later REST call and the hub
/// connection are that character because of the credential they carry, not because of anything the
/// client attaches per request. The hub is reconnected so it re-authenticates with the new token, and
/// the command terminal ends the previous character's session and connects as the new one.
/// </summary>
/// <remarks>
/// The play terminal follows in one of two ways. On <c>/play</c> the tab has one character session, and its
/// connection ends and connects again as the new character. Once the tab has been on <c>/client</c>
/// (<see cref="CharacterSessionRegistry.KeepsSessions"/>), or when the character already has a session in the tab,
/// the character's own session comes to the front, opened and connected first when it has none, and every other
/// session stays connected.
/// <para>
/// A switch returns once the tab acts as the new character and its play terminal is in front. The command
/// terminal and the hub follow behind it (<see cref="Settled"/>), so another switch can start straight away; when
/// several switches land while they are still following, they follow only the last.
/// </para>
/// </remarks>
public class CharacterSwitchService(
	AccountAuthService accountAuth,
	TerminalServiceHost commandTerminal,
	CharacterSessionRegistry sessions,
	IConnectionStateService connectionState,
	TerminalResumeStore resumePoints,
	NavigationManager navigation,
	ILogger<CharacterSwitchService> logger)
{
	/// <summary>One switch at a time: two that each saw a free place in a full tab would otherwise both take it.</summary>
	private readonly SemaphoreSlim _switching = new(1, 1);

	private readonly Lock _followGate = new();
	private Task _following = Task.CompletedTask;
	private bool _followRunning;
	private bool _followAgain;

	/// <summary>The character the command terminal is to connect as next; null leaves it as it is.</summary>
	private AccountAuthService.CharacterSummary? _commandCharacter;

	/// <summary>A one-time token for <see cref="_commandCharacter"/> the switch did not use; null mints one.</summary>
	private string? _commandOtt;

	/// <summary>The command terminal is to hang up rather than connect again.</summary>
	private bool _hangUpCommand;

	/// <summary>Whether a sign-out reaches the command terminal through <see cref="Follow"/>; from the first switch on.</summary>
	private bool _watchingSignOut;

	/// <summary>Completes once the command terminal and the hub have caught up with the last switch.</summary>
	public Task Settled
	{
		get
		{
			lock (_followGate) return _following;
		}
	}

	/// <summary>
	/// Returns false when the switch did not happen: the server refused it, the tab keeps characters and has no
	/// room for another, or the new character could not connect. The tab keeps its current identity; when the
	/// server will not give that back either, the terminal in front and the command terminal are hung up, so
	/// nothing sends as a character the tab no longer acts as.
	/// </summary>
	public async Task<bool> SwitchAsync(AccountAuthService.CharacterSummary character)
	{
		await _switching.WaitAsync();
		try
		{
			return await SwitchOneAsync(character);
		}
		finally
		{
			_switching.Release();
		}
	}

	private async Task<bool> SwitchOneAsync(AccountAuthService.CharacterSummary character)
	{
		// A full /client tab would otherwise play the new character on the session in front, logging that one out.
		if (sessions.KeepsSessions && !sessions.CanOpen && SessionOf(character) is null) return false;
		var previous = accountAuth.ActiveCharacter;
		if (await accountAuth.SwitchCharacterAsync(character) is not string ott) return false;

		// The switch's own OTT goes to the play terminal when it has to log in; the command terminal then mints its own,
		// since a terminal consumes its OTT on connect. A character already connected in the tab leaves the OTT to the
		// command terminal: minting is rate limited per client, and a quick run of switches would otherwise use it up.
		// The tab already holds the new token: the hub re-authenticates with it even if a terminal fails.
		bool ottUnused;
		try
		{
			ottUnused = await PlayAsAsync(character, ott);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			logger.LogWarning(ex, "Could not connect as {Character}", character.Name);
			// The new character could not connect: the tab goes back to acting as the one still in front, so the
			// website and the play terminal name the same character.
			if (previous is not null && await accountAuth.SwitchCharacterAsync(previous) is string)
			{
				Follow();
				return false;
			}

			// Could not go back either: the terminal in front and the command terminal would go on sending as a
			// character the tab no longer acts as.
			await HangUpAsync(sessions.Focused.Terminal);
			lock (_followGate)
			{
				(_commandCharacter, _commandOtt) = (null, null);
				_hangUpCommand = true;
			}

			Follow();
			return false;
		}

		lock (_followGate)
		{
			(_commandCharacter, _commandOtt) = (character, ottUnused ? ott : null);
			_hangUpCommand = false;
		}

		Follow();
		return true;
	}

	/// <summary>
	/// Brings the command terminal and the hub up to the last switch, behind it. A switch that lands while they are
	/// still following one makes them go round once more, for whatever it left.
	/// </summary>
	private void Follow()
	{
		lock (_followGate)
		{
			if (!_watchingSignOut)
			{
				accountAuth.AuthStateChanged += OnAuthStateChanged;
				_watchingSignOut = true;
			}

			if (_followRunning)
			{
				_followAgain = true;
				return;
			}

			_followRunning = true;
			_following = FollowAsync();
		}
	}

	/// <summary>
	/// A sign-out hangs the command terminal up behind whatever it is still doing for a switch. Sign-out's own
	/// teardown closes the connections open when it runs; one a switch was still opening connects after it.
	/// </summary>
	private void OnAuthStateChanged()
	{
		if (accountAuth.IsLoggedIn) return;
		lock (_followGate)
		{
			(_commandCharacter, _commandOtt) = (null, null);
			_hangUpCommand = true;
		}

		Follow();
	}

	private async Task FollowAsync()
	{
		// Out of the caller's lock and its switch: the switch has returned before anything here runs.
		await Task.Yield();
		while (true)
		{
			AccountAuthService.CharacterSummary? character;
			string? ott;
			bool hangUp;
			lock (_followGate)
			{
				(character, ott, hangUp) = (_commandCharacter, _commandOtt, _hangUpCommand);
				(_commandCharacter, _commandOtt, _hangUpCommand, _followAgain) = (null, null, false, false);
			}

			try
			{
				if (hangUp) await HangUpAsync(commandTerminal);
				else if (character is not null) await RebindAsync(commandTerminal, character, ott);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				logger.LogWarning(ex, "Could not move the command terminal to {Character}", character?.Name);
			}

			try
			{
				// Signed out, the hub has no one to authenticate as.
				if (accountAuth.IsLoggedIn) await connectionState.ReconnectAsync();
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				logger.LogWarning(ex, "Could not reconnect the game hub after a switch");
			}

			lock (_followGate)
			{
				if (_followAgain) continue;
				_followRunning = false;
				return;
			}
		}
	}

	/// <summary>
	/// Puts <paramref name="character"/>'s play session in front, logged in with <paramref name="ott"/> when it needs to
	/// be. True when the session was already there, and the OTT is left unused.
	/// </summary>
	private async Task<bool> PlayAsAsync(AccountAuthService.CharacterSummary character, string ott)
	{
		if (SessionOf(character) is { } kept)
		{
			sessions.Focus(kept);
			return true;
		}

		if (sessions.KeepsSessions && sessions.CanOpen)
		{
			// Connected before it comes to the front, so Play finds it logging in and does not log it in again.
			var opened = sessions.Open();
			try
			{
				await ConnectAsync(opened.Terminal, character, ott);
			}
			catch
			{
				// Not left holding a place in the tab for a character that never connected.
				await sessions.CloseAsync(opened);
				throw;
			}

			sessions.Focus(opened);
			return false;
		}

		// No terminal may leave a resume point for the previous character behind: a later reload would
		// resume that session instead of connecting as the character the tab acts as now. Not when other
		// sessions are kept: each resumes its own character on a reload.
		if (sessions.Sessions.Count == 1) await resumePoints.ClearAllAsync();
		await RebindAsync(sessions.Focused.Terminal, character, ott);
		return false;
	}

	/// <summary>Ends <paramref name="terminal"/>'s session, if it has one, without connecting it again.</summary>
	private async Task HangUpAsync(TerminalServiceHost terminal)
	{
		if (!terminal.IsConnected) return;
		try
		{
			await terminal.SendAsync("QUIT");
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			logger.LogWarning(ex, "Could not send QUIT before hanging up");
		}

		await terminal.DisconnectAsync();
	}

	private CharacterSession? SessionOf(AccountAuthService.CharacterSummary character) =>
		TerminalIdentity.Of(accountAuth.Username, character) is { } identity && sessions.For(identity) is CharacterSession kept
			? kept
			: null;

	/// <summary>
	/// Ends <paramref name="terminal"/>'s session and, when it was connected, connects it again as
	/// <paramref name="character"/>. A terminal that was not connected is only rebuilt: it connects as the
	/// active character whenever it next does.
	/// </summary>
	private async Task RebindAsync(TerminalServiceHost terminal, AccountAuthService.CharacterSummary character, string? ott)
	{
		var wasConnected = terminal.IsConnected;
		if (wasConnected)
		{
			// QUIT, not a bare close: the connection server holds a dropped socket's session for a grace
			// window, during which the previous character would still be listed as online.
			await terminal.SendAsync("QUIT");
			await terminal.DisconnectAsync();
		}

		await terminal.RecreateAsync();
		if (!wasConnected) return;

		if (ott is null)
		{
			var account = accountAuth.Username;
			if (await accountAuth.GetOttForCharacterAsync(character) is not string minted) return;
			// Signed out (or signed in as another account) while the token was minted: the session it was minted for is over.
			if (!accountAuth.IsLoggedIn || accountAuth.Username != account) return;
			ott = minted;
		}

		await ConnectAsync(terminal, character, ott);
	}

	private Task ConnectAsync(TerminalServiceHost terminal, AccountAuthService.CharacterSummary character, string ott)
	{
		terminal.ConnectedPlayerName = character.Name;
		return terminal.ConnectWithOttAsync(TerminalEndpoint.Resolve(navigation.BaseUri), ott,
			TerminalIdentity.Of(accountAuth.Username, character));
	}
}
