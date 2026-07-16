namespace SharpMUSH.Library.Models;

/// <summary>
/// Game-wide server state — a single document per game (fixed key), not per account.
/// </summary>
public class SharpServerState
{
	/// <summary>
	/// True once first-run setup has been completed (setup wizard, or God's character
	/// password set via @password/@newpassword in-game). While false, the web portal
	/// shows the first-run wizard.
	/// </summary>
	public bool SetupCompleted { get; set; }
}
