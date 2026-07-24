using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Switches the portal's active character: commits it as the acting identity and reconnects the game
/// hub so live SignalR rebinds to it (REST follows it live via the acting-character header). Does not
/// touch the terminals — a terminal's character is fixed at connect time; open a new tab to play a
/// different character.
/// </summary>
public class CharacterSwitchService(AccountAuthService accountAuth, IConnectionStateService connectionState)
{
	public async Task SwitchAsync(AccountAuthService.CharacterSummary character)
	{
		accountAuth.SetActiveCharacter(character);
		await connectionState.ReconnectAsync();
	}
}
