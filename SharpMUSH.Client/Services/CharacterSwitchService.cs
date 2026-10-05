using Microsoft.AspNetCore.Components;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Switches the portal's acting character. The switch is a server-side rebind: the endpoint mints a
/// session token bound to the target and this tab adopts it, so every later REST call and the hub
/// connection are that character because of the credential they carry, not because of anything the
/// client attaches per request. The hub is reconnected so it re-authenticates with the new token, and
/// every terminal that was connected ends the previous character's session and connects as the new one,
/// so the terminal and the Play page follow the switch.
/// </summary>
public class CharacterSwitchService(
	AccountAuthService accountAuth,
	TerminalServiceHost commandTerminal,
	PlayTerminalServiceHost playTerminal,
	IConnectionStateService connectionState,
	TerminalResumeStore resumePoints,
	NavigationManager navigation)
{
	/// <summary>Returns false when the server refused the switch; the tab keeps its current identity.</summary>
	public async Task<bool> SwitchAsync(AccountAuthService.CharacterSummary character)
	{
		if (await accountAuth.SwitchCharacterAsync(character) is not string ott) return false;

		// No terminal may leave a resume point for the previous character behind: a later reload would
		// resume that session instead of connecting as the character the tab acts as now.
		await resumePoints.ClearAllAsync();

		// The switch's own OTT goes to the play terminal; the command terminal mints its own, since a
		// terminal consumes its OTT on connect.
		// The tab already holds the new token: the hub re-authenticates with it even if a terminal fails.
		try
		{
			await RebindAsync(playTerminal, character, ott);
			await RebindAsync(commandTerminal, character, null);
		}
		finally
		{
			await connectionState.ReconnectAsync();
		}

		return true;
	}

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

		terminal.ConnectedPlayerName = character.Name;
		await terminal.ConnectWithOttAsync(TerminalEndpoint.Resolve(navigation.BaseUri), ott,
			TerminalIdentity.Of(accountAuth.Username, character));
	}
}
