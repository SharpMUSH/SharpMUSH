using Microsoft.AspNetCore.Components;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Connects the account's characters on <c>/client</c>, so the rail has them all without the player adding each one:
/// the character the tab acts as connects as Play always does, and the others connect in the background, resuming
/// their sessions when the server still holds them. A tab keeps at most <see cref="CharacterSessionRegistry.Limit"/>.
/// </summary>
public sealed class ClientCharacters(
	CharacterSessionRegistry sessions,
	AccountAuthService accountAuth,
	NavigationManager navigation,
	ILogger<ClientCharacters> logger)
{
	private Task? _connecting;

	/// <summary>The account whose characters this page load has connected; another account signing in connects its own.</summary>
	private string? _connectedFor;

	/// <summary>
	/// The characters the rail shows, in the account's order: the first <see cref="CharacterSessionRegistry.Limit"/>.
	/// </summary>
	public IReadOnlyList<AccountAuthService.CharacterSummary> Shown =>
		accountAuth.IsLoggedIn ? accountAuth.Characters.Take(CharacterSessionRegistry.Limit).ToList() : [];

	/// <summary>
	/// Connects every shown character that has no session yet, once per page load for each account that signs in. A
	/// character logged out after that stays out until the player picks it again. Until the account is signed in and the character it acts as is
	/// known, nothing is done and a later call tries again.
	/// </summary>
	public async Task ConnectAllAsync()
	{
		if (_connectedFor is not null && _connectedFor == accountAuth.Username) return;

		_connecting ??= ConnectOnceAsync();
		try
		{
			await _connecting;
		}
		finally
		{
			_connecting = null;
		}
	}

	private async Task ConnectOnceAsync()
	{
		await accountAuth.InitAsync();
		// Nothing to connect for a visitor; a later sign-in in this tab asks again.
		if (!accountAuth.IsLoggedIn) return;
		if (accountAuth.Characters.Count == 0 && await accountAuth.GetCharactersAsync() is ApiFailure) return;
		// The character in front connects through Play; until the tab knows which one that is, any could be it.
		if (accountAuth.ActiveCharacter?.Objid is not string acting) return;

		_connectedFor = accountAuth.Username;
		foreach (var character in Shown)
		{
			if (character.Objid == acting || !sessions.CanOpen) continue;
			if (TerminalIdentity.Of(accountAuth.Username, character) is not { } identity) continue;
			if (sessions.For(identity) is CharacterSession) continue;

			await ConnectAsync(character, identity);
		}
	}

	private async Task ConnectAsync(AccountAuthService.CharacterSummary character, TerminalIdentity identity)
	{
		var session = sessions.Open();
		try
		{
			if (await accountAuth.GetOttForCharacterAsync(character) is not string ott)
			{
				await sessions.CloseAsync(session);
				return;
			}

			// Signing out closes every background session; a token that arrives after that connects none.
			if (!sessions.Sessions.Contains(session)) return;

			session.Terminal.ConnectedPlayerName = character.Name;
			await session.Terminal.ConnectWithOttAsync(TerminalEndpoint.Resolve(navigation.BaseUri), ott, identity);
			// Closed while it connected: the character leaves the game again.
			if (!sessions.Sessions.Contains(session)) await QuitAsync(session);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// The gateway may be down; the character stays on the rail, disconnected, as a dropped one does.
			logger.LogWarning(ex, "Could not connect {Character} in the background", character.Name);
		}
	}

	private async Task QuitAsync(CharacterSession session)
	{
		if (!session.Terminal.IsConnected) return;
		await session.Terminal.SendAsync("QUIT");
		await session.Terminal.DisconnectAsync();
	}
}
