using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Keeps what a login tells its player on the connection that logged in. Outside a login, what a player
/// is told reaches every connection they have; the last-connect lines and the look a login ends with are
/// about that one connection, and a second connection (another client, or the portal's other terminal)
/// would otherwise see each login's copy.
/// </summary>
public static class LoginOutput
{
	private static readonly AsyncLocal<(int Player, long Handle)?> Current = new();

	/// <summary>
	/// Until disposed, what <paramref name="player"/> is told on this async flow goes only to
	/// <paramref name="handle"/>. Everyone else is told as usual.
	/// </summary>
	public static IDisposable To(DBRef player, long handle)
	{
		var previous = Current.Value;
		Current.Value = (player.Number, handle);
		return new Restore(previous);
	}

	/// <summary>Whether a message for <paramref name="player"/> skips their connection <paramref name="handle"/>.</summary>
	public static bool Skips(DBRef player, long handle) =>
		Current.Value is { } scope && scope.Player == player.Number && scope.Handle != handle;

	private sealed class Restore((int Player, long Handle)? previous) : IDisposable
	{
		public void Dispose() => Current.Value = previous;
	}
}
