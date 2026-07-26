using Microsoft.AspNetCore.Components;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Connects the command terminal as a specific character at initial login (the single-character
/// auto-login and the <c>?as=</c> new-tab entry). Mints the character's OTT, commits it as the active
/// character, and opens the terminal socket. Separate from <see cref="CharacterSwitchService"/>, which
/// switches the PORTAL identity and never touches the terminal — a terminal's character is fixed once
/// it connects.
/// </summary>
public class TerminalLoginService(
	ITerminalService terminal, AccountAuthService accountAuth, NavigationManager navigation)
{
	public async Task<bool> ConnectAsCharacterAsync(AccountAuthService.CharacterSummary character)
	{
		var ott = await accountAuth.GetOttForCharacterAsync(character);
		if (ott is null) return false;

		accountAuth.SetActiveCharacter(character);
		terminal.ConnectedPlayerName = character.Name;
		await terminal.ConnectWithOttAsync(ServerUri, ott);
		return true;
	}

	public string ServerUri => TerminalEndpoint.Resolve(navigation.BaseUri);
}
