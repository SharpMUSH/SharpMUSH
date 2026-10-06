using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A fresh login token, <see cref="NotFound"/> when this tab can no longer have one (it signed out, or the
/// character is gone), or the <see cref="ApiFailure"/> that stopped the server being asked.
/// </summary>
public union TerminalLoginMint(string, NotFound, ApiFailure);

/// <summary>Mints the one-time token that logs a terminal's character in again.</summary>
public interface ITerminalLoginTokens
{
	/// <summary>A fresh token for <paramref name="identity"/>; see <see cref="TerminalLoginMint"/>.</summary>
	Task<TerminalLoginMint> MintAsync(TerminalIdentity identity);
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
	public async Task<TerminalLoginMint> MintAsync(TerminalIdentity identity)
	{
		var auth = services.GetRequiredService<AccountAuthService>();
		if (auth.Username != identity.Account) return new NotFound();

		if (auth.Characters.Count == 0 && await auth.GetCharactersAsync() is ApiFailure rosterFailure)
			return rosterFailure;
		if (auth.Characters.FirstOrDefault(c => TerminalIdentity.Of(identity.Account, c) == identity) is { } character)
			return await auth.GetOttForCharacterAsync(character) switch
			{
				string ott => ott,
				ApiFailure failure => failure,
			};

		if (!auth.HasDebugOtt || (await auth.GetDebugOttAsync())?.PlayerName != identity.Character) return new NotFound();
		// The cached debug token was spent on the first login.
		auth.InvalidateDebugOtt();
		return (await auth.GetDebugOttAsync())?.Token is { } token ? token : new NotFound();
	}
}
