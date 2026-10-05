using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Switches the character this tab plays. The switch is a server-side rebind: the endpoint mints a
/// session token bound to the target and this tab adopts it, so every later REST call and the hub
/// connection are that character because of the credential they carry, not because of anything the
/// client attaches per request. The hub is reconnected so it re-authenticates with the new token, and
/// every terminal still connected as the previous character quits and connects again as the new one:
/// left alone, Play went on as the old character while the rest of the portal named the new one.
/// </summary>
public class CharacterSwitchService(
	AccountAuthService accountAuth,
	IConnectionStateService connectionState,
	TerminalResumeStore resumePoints,
	TerminalServiceHost commandTerminal,
	PlayTerminalServiceHost playTerminal,
	NavigationManager navigation,
	ILogger<CharacterSwitchService> logger)
{
	/// <summary>Returns false when the server refused the switch; the tab keeps its current identity.</summary>
	public async Task<bool> SwitchAsync(AccountAuthService.CharacterSummary character)
	{
		var previous = accountAuth.ActiveCharacter;
		if (await accountAuth.SwitchCharacterAsync(character) is not { } ott) return false;

		// The old character's sessions may not leave a resume point behind: a later reload connects as
		// the character the tab acts as now.
		await resumePoints.ClearAllAsync();
		await connectionState.ReconnectAsync();

		if (previous is not null && previous.DbrefNumber == character.DbrefNumber && previous.CreationTime == character.CreationTime)
			return true;

		var serverUri = TerminalEndpoint.Resolve(navigation.BaseUri);
		var identity = TerminalIdentity.Of(accountAuth.Username, character);
		// The switch's own OTT goes to the command terminal; one is consumed per connect, so Play mints its own.
		await MoveAsync(commandTerminal, character, ott, serverUri, identity);
		await MoveAsync(playTerminal, character, null, serverUri, identity);
		return true;
	}

	/// <summary>
	/// Ends a connected terminal's session as the previous character and opens it again as
	/// <paramref name="character"/>. A terminal that is not connected is left for its page to connect,
	/// which it does as the active character.
	/// </summary>
	private async Task MoveAsync(TerminalServiceHost terminal, AccountAuthService.CharacterSummary character,
		string? ott, string serverUri, TerminalIdentity? identity)
	{
		if (!terminal.IsConnected) return;

		try
		{
			ott ??= await accountAuth.GetOttForCharacterAsync(character);

			// QUIT, not a bare close: the connection server holds a dropped socket's character online for
			// a grace window (see TerminalSessionTeardown).
			await terminal.SendAsync("QUIT");
			await terminal.DisconnectAsync();
			// A fresh inner terminal: a surviving resume token would rebind the socket to the old session.
			await terminal.RecreateAsync();

			if (ott is null) return;
			terminal.ConnectedPlayerName = character.Name;
			await terminal.ConnectWithOttAsync(serverUri, ott, identity);
		}
		catch (Exception ex)
		{
			// The gateway can be down; the portal half has switched, and the terminal's own Connect stays.
			logger.LogWarning(ex, "Could not move a terminal to #{Dbref}", character.DbrefNumber);
		}
	}
}
