using Microsoft.AspNetCore.Components;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Connects the command terminal as a specific character at initial login (the character picker, the
/// single-character auto-login, and the <c>?as=</c> new-tab entry). Mints the character's OTT, commits
/// it as the active character, and opens the terminal socket. Separate from
/// <see cref="CharacterSwitchService"/>, which switches the PORTAL identity and never touches the
/// terminal — a terminal's character is fixed once it connects.
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

	/// <summary>
	/// The terminal WebSocket endpoint, derived from the portal's own origin so it survives a reverse
	/// proxy. Loopback maps to the dev connection server on :4202.
	/// </summary>
	public string ServerUri
	{
		get
		{
			var baseUri = new Uri(navigation.BaseUri);
			if (baseUri.IsLoopback)
				return "ws://localhost:4202/ws";
			var scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
			return $"{scheme}://{baseUri.Authority}/ws";
		}
	}
}
