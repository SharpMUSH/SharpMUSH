using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Switches the portal's acting character. The switch is a server-side rebind: the endpoint mints a
/// session token bound to the target and this tab adopts it, so every later REST call and the hub
/// connection are that character because of the credential they carry, not because of anything the
/// client attaches per request. The hub is reconnected so it re-authenticates with the new token.
/// Does not touch the terminals — a terminal's character is fixed at connect time; open a new tab to
/// play a different character.
/// </summary>
public class CharacterSwitchService(AccountAuthService accountAuth, IConnectionStateService connectionState)
{
	/// <summary>Returns false when the server refused the switch; the tab keeps its current identity.</summary>
	public async Task<bool> SwitchAsync(AccountAuthService.CharacterSummary character)
	{
		// The OTT is for terminals, which this service deliberately does not touch — only whether the
		// switch took matters here.
		if (await accountAuth.SwitchCharacterAsync(character) is null) return false;

		await connectionState.ReconnectAsync();
		return true;
	}
}
