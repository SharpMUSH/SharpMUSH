using Microsoft.AspNetCore.Components;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Promotes the session to a real character — used when a character is created (a guest, or an account
/// creating its first character), so the player starts playing that character everywhere at once.
/// </summary>
public interface ICharacterUpgradeService
{
	/// <summary>
	/// Commits <paramref name="character"/> as active and connects both terminals + the game hub as it.
	/// Returns <c>false</c> (touching nothing) if an OTT could not be minted.
	/// </summary>
	Task<bool> PlayAsAsync(AccountAuthService.CharacterSummary character);
}

/// <summary>
/// Mints a per-terminal OTT, commits the active character, cleanly recreates and reconnects BOTH
/// terminals (command and /play) as the character, and reconnects the game hub.
/// </summary>
public class CharacterUpgradeService(
	AccountAuthService accountAuth,
	TerminalServiceHost commandTerminal,
	PlayTerminalServiceHost playTerminal,
	IConnectionStateService connectionState,
	NavigationManager navigation) : ICharacterUpgradeService
{
	public async Task<bool> PlayAsAsync(AccountAuthService.CharacterSummary character)
	{
		// A terminal consumes its OTT on connect, so each needs its own.
		if (await accountAuth.GetOttForCharacterAsync(character) is not string commandOtt
			|| await accountAuth.GetOttForCharacterAsync(character) is not string playOtt)
			return false;

		accountAuth.SetActiveCharacter(character);

		var serverUri = TerminalEndpoint.Resolve(navigation.BaseUri);

		// Recreate first: a surviving guest resume token would otherwise rebind the socket to the old
		// guest session and discard the new OTT.
		await commandTerminal.RecreateAsync();
		await playTerminal.RecreateAsync();

		commandTerminal.ConnectedPlayerName = character.Name;
		playTerminal.ConnectedPlayerName = character.Name;

		var identity = TerminalIdentity.Of(accountAuth.Username, character);
		await commandTerminal.ConnectWithOttAsync(serverUri, commandOtt, identity);
		await playTerminal.ConnectWithOttAsync(serverUri, playOtt, identity);

		await connectionState.ReconnectAsync();
		return true;
	}
}
