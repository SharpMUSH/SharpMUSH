using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Client.Services;

/// <summary>Mints the one-time token that logs a terminal's character in again.</summary>
public interface ITerminalLoginTokens
{
	/// <summary>A fresh token for <paramref name="identity"/>, or null when this tab can no longer have one.</summary>
	Task<string?> MintAsync(TerminalIdentity identity);
}

/// <summary>
/// Mints through the account session: the identity's character, while the tab is still signed in to that
/// account. The development auto-login, whose identity names the debug player, mints a new debug token.
/// </summary>
/// <remarks>
/// <see cref="AccountAuthService"/> is resolved on use: it is built with the session-ending handlers, which
/// hold the terminals this is handed to.
/// </remarks>
public sealed class AccountTerminalLoginTokens(IServiceProvider services) : ITerminalLoginTokens
{
	public async Task<string?> MintAsync(TerminalIdentity identity)
	{
		var auth = services.GetRequiredService<AccountAuthService>();
		if (auth.Username != identity.Account) return null;

		if (auth.Characters.Count == 0)
			await auth.GetCharactersAsync();
		if (auth.Characters.FirstOrDefault(c => TerminalIdentity.Of(identity.Account, c) == identity) is { } character)
			return await auth.GetOttForCharacterAsync(character) is string ott ? ott : null;

		if (!auth.HasDebugOtt || (await auth.GetDebugOttAsync())?.PlayerName != identity.Character) return null;
		// The cached debug token was spent on the first login.
		auth.InvalidateDebugOtt();
		return (await auth.GetDebugOttAsync())?.Token;
	}
}
