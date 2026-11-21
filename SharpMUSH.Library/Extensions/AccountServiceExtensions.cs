using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Extensions;

public static class AccountServiceExtensions
{
	/// <summary>
	/// Whether <paramref name="executor"/> is <paramref name="player"/> itself, or another character on
	/// <paramref name="holder"/>, the account holding the player.
	/// </summary>
	public static async ValueTask<bool> SharesAccountAsync(this IAccountService accounts, AnySharpObject executor,
		SharpPlayer player, SharpAccount? holder, CancellationToken cancellationToken = default)
		=> executor.Object().DBRef.Number == player.Object.DBRef.Number
			|| (holder is { Id: { } id } && executor.IsPlayer
				&& (await accounts.GetAccountForCharacterAsync(executor.Object().DBRef, cancellationToken))?.Id == id);
}
