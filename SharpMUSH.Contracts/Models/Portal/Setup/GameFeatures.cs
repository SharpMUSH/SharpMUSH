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

/// <summary>The handler objects a game's softcode packages attach to.</summary>
public static class HandlerKinds
{
	/// <summary><c>http_handler</c>: the object whose attributes answer <c>/http/</c> requests.</summary>
	public const string Http = "http";

	/// <summary><c>event_handler</c>: the object whose attributes the engine triggers on game events.</summary>
	public const string Event = "event";
}

/// <summary>One handler as the setup wizard shows it.</summary>
/// <param name="Kind">A <see cref="HandlerKinds"/> value.</param>
/// <param name="Dbref">The configured object's number, or null when none is configured.</param>
/// <param name="Name">The configured object's name; null when none is configured or it does not exist.</param>
/// <param name="IsWizard">Whether that object has the WIZARD flag, which the packages built on it need.</param>
/// <param name="Packages">The installed bundled packages that attach to this handler.</param>
public record HandlerState(string Kind, int? Dbref, string? Name, bool IsWizard, IReadOnlyList<string> Packages);

/// <summary>
/// An attribute a bundled package writes to a handler that the object already has from somewhere else. The
/// install keeps the object's own, so that part of the package runs the game's code instead of its own.
/// </summary>
/// <param name="Package">The bundled package that writes it.</param>
/// <param name="Attribute">The attribute's name.</param>
public record HandlerClash(string Package, string Attribute);

/// <summary>How the wizard sets a handler.</summary>
public static class HandlerModes
{
	/// <summary>Make the object at <see cref="SetHandlerRequest.Dbref"/> the handler, building on what it has.</summary>
	public const string Use = "use";

	/// <summary>Create a new WIZARD thing in the master room and make it the handler.</summary>
	public const string Create = "create";

	/// <summary>Leave the game without this handler.</summary>
	public const string None = "none";
}

/// <param name="Mode">A <see cref="HandlerModes"/> value.</param>
/// <param name="Dbref">The object to use, for <see cref="HandlerModes.Use"/>.</param>
public record SetHandlerRequest(string Mode, int? Dbref = null);

/// <summary>A package the server ships, as the setup wizard offers it.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Description">The manifest's description.</param>
/// <param name="Installed">Whether the game has it.</param>
/// <param name="Requires">The <see cref="HandlerKinds"/> value of the handler it attaches to, or null.</param>
/// <param name="Available">
/// Whether it can be installed now: false while the handler it attaches to, or the plugin it calls into, is missing.
/// </param>
/// <param name="DependsOn">The bundled packages it needs installed first.</param>
/// <param name="Recommended">
/// Whether a new game has it: one installed at first boot that the administrator has not turned off. A game
/// without it — after a PennMUSH import, which removes the handlers it was built on — is offered it ticked.
/// </param>
public record BundledPackageState(string Id, string Description, bool Installed, string? Requires, bool Available,
	IReadOnlyList<string> DependsOn, bool Recommended = false);

/// <summary>The first-run wizard's state after the game has been claimed.</summary>
/// <param name="Pending">True from the claim until the administrator finishes or dismisses the wizard.</param>
public record SetupWizardResponse(bool Pending, IReadOnlyList<HandlerState> Handlers,
	IReadOnlyList<BundledPackageState> Packages);

/// <summary>The bundled packages the administrator wants installed; every other bundled package is removed.</summary>
public record SetupPackagesRequest(IReadOnlyList<string> Installed);
