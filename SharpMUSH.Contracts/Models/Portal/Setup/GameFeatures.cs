namespace SharpMUSH.Library.Models.Portal.Setup;

/// <summary>
/// The optional applications a game can switch on or off, by the id the server reports them under
/// (<c>api/server-info</c>'s <c>Features</c>) and the first-run wizard offers them under. The portal
/// links to an application's pages only while its id is in that list.
/// </summary>
public static class GameFeatures
{
	/// <summary>
	/// The Scene System: pose capture into scene logs, the <c>+scene</c> verbs, the scene archive and
	/// live scenes in the portal. The <c>scene</c> package over the Scene plugin.
	/// </summary>
	public const string Scenes = "scenes";

	/// <summary>The in-game wiki reader: <c>+wiki</c> listings and articles from the portal's wiki.</summary>
	public const string WikiReader = "wiki-reader";

	/// <summary>
	/// What a new game has before anyone chooses — the applications the server installs at first boot.
	/// Also what the portal assumes when it cannot ask the server.
	/// </summary>
	public static readonly IReadOnlyList<string> Defaults = [Scenes];
}

/// <summary>One optional application as the setup wizard shows it.</summary>
/// <param name="Id">A <see cref="GameFeatures"/> id.</param>
/// <param name="Enabled">Whether the game has it now.</param>
/// <param name="Available">
/// Whether this server can switch it on: false when what it runs on (a plugin, a handler object) is
/// missing from this install.
/// </param>
public record OptionalApplicationState(string Id, bool Enabled, bool Available);

/// <summary>The first-run wizard's state after the game has been claimed.</summary>
/// <param name="Pending">True from the claim until the administrator finishes or dismisses the wizard.</param>
public record SetupWizardResponse(bool Pending, IReadOnlyList<OptionalApplicationState> Applications);

/// <summary>The applications the administrator wants on; every other optional application goes off.</summary>
public record SetupApplicationsRequest(IReadOnlyList<string> Enabled);
