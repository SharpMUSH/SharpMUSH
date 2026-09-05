using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library;

/// <summary>
/// What makes a player a guest, in one place.
/// </summary>
/// <remarks>
/// Two callers ask this question about the same players: <c>SocketCommands.HandleGuestLogin</c>,
/// picking a character for <c>connect guest</c>, and the portal's guest admin panel, showing an
/// operator which characters that command can hand out. If those two ever disagreed the panel would
/// be lying about the only thing it exists to report — an operator would stock the game with
/// characters guest login refuses to use, or delete ones it is still using.
/// </remarks>
public static class GuestCharacters
{
	/// <summary>The power a player must carry to be handed out by <c>connect guest</c>.</summary>
	public const string GuestPower = "Guest";

	/// <summary>Whether <paramref name="player"/> is one of the game's guest characters.</summary>
	public static ValueTask<bool> IsGuestAsync(SharpPlayer player)
		=> player.Object.HasPower(GuestPower);

	/// <summary>Every guest character in the game, in database order.</summary>
	public static IAsyncEnumerable<SharpPlayer> AllAsync(IMediator mediator)
		=> mediator.CreateStream(new GetAllPlayersQuery())
			.Where(async (player, _) => await IsGuestAsync(player));
}
