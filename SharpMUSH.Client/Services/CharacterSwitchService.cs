using Microsoft.AspNetCore.Components;
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
/// </remarks>
public class CharacterSwitchService(
	AccountAuthService accountAuth,
	TerminalServiceHost commandTerminal,
	CharacterSessionRegistry sessions,
	IConnectionStateService connectionState,
	TerminalResumeStore resumePoints,
	NavigationManager navigation)
{
	/// <summary>
	/// Returns false when the switch did not happen: the server refused it, or the tab keeps characters and has no
	/// room for another. The tab keeps its current identity.
	/// </summary>
	public async Task<bool> SwitchAsync(AccountAuthService.CharacterSummary character)
	{
		// A full /client tab would otherwise play the new character on the session in front, logging that one out.
		if (sessions.KeepsSessions && !sessions.CanOpen && SessionOf(character) is null) return false;
		if (await accountAuth.SwitchCharacterAsync(character) is not string ott) return false;

		// The switch's own OTT goes to the play terminal; the command terminal mints its own, since a
		// terminal consumes its OTT on connect.
		// The tab already holds the new token: the hub re-authenticates with it even if a terminal fails.
		try
		{
			await PlayAsAsync(character, ott);
			await RebindAsync(commandTerminal, character, null);
		}
		finally
		{
			await connectionState.ReconnectAsync();
		}

		return true;
	}

	/// <summary>Puts <paramref name="character"/>'s play session in front, logged in with <paramref name="ott"/> when it needs to be.</summary>
	private async Task PlayAsAsync(AccountAuthService.CharacterSummary character, string ott)
	{
		if (SessionOf(character) is { } kept)
		{
			sessions.Focus(kept);
			return;
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
			return;
		}

		// No terminal may leave a resume point for the previous character behind: a later reload would
		// resume that session instead of connecting as the character the tab acts as now. Not when other
		// sessions are kept: each resumes its own character on a reload.
		if (sessions.Sessions.Count == 1) await resumePoints.ClearAllAsync();
		await RebindAsync(sessions.Focused.Terminal, character, ott);
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
			if (await accountAuth.GetOttForCharacterAsync(character) is not string minted) return;
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
