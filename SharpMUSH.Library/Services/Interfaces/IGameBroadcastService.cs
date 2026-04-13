namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Handles GAME:-prefixed broadcast messages sent to all connected players.
/// Mirrors PennMUSH's broadcast() / flag_broadcast() from src/bsd.c.
/// Examples: "GAME: Shutdown by {name}", "GAME: Save complete.", connect/disconnect messages.
/// </summary>
public interface IGameBroadcastService
{
	/// <summary>
	/// Broadcast a message to all connected (logged-in) players.
	/// Equivalent to PennMUSH's <c>flag_broadcast(0, 0, T("..."))</c>.
	/// </summary>
	ValueTask BroadcastAsync(string message);

	/// <summary>
	/// Broadcast a message only to connected players who have a specific flag.
	/// Equivalent to PennMUSH's <c>flag_broadcast("WIZARD", 0, T("..."))</c>.
	/// </summary>
	/// <param name="flagName">Flag that the recipient must have (e.g., "WIZARD").</param>
	/// <param name="message">The message text to send.</param>
	ValueTask BroadcastToFlagAsync(string flagName, string message);

	/// <summary>
	/// Broadcast a shutdown or reboot notification to all connected players.
	/// Uses <see cref="SharpMUSH.Library.Definitions.ErrorMessages.Notifications"/> constants.
	/// </summary>
	/// <param name="adminName">Name of the admin initiating the action.</param>
	/// <param name="isReboot">True for reboot, false for shutdown.</param>
	ValueTask BroadcastShutdownAsync(string adminName, bool isReboot);
}
