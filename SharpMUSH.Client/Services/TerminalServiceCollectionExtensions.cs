namespace SharpMUSH.Client.Services;

/// <summary>
/// Registers the terminal facades as DI singletons. Extracted from <c>Program.cs</c> into a
/// real production method so the registration shape itself can be exercised by tests directly, rather
/// than re-typed inside a test fixture where it could silently drift from what actually runs.
/// </summary>
public static class TerminalServiceCollectionExtensions
{
	/// <summary>
	/// Registers both terminal connections as stable facades. Each holds a swappable inner
	/// terminal so a character switch can dispose and rebuild the connection — every <c>@inject</c>
	/// site and <see cref="MushQueryService"/>'s constructor capture keep pointing at the facade, which
	/// never changes identity. The concrete facade type is also registered directly (aliased to the
	/// same instance) so the character-switch flow can call <c>RecreateAsync()</c> without casting from
	/// the interface.
	/// </summary>
	public static IServiceCollection AddTerminalServices(this IServiceCollection services)
	{
		// IWebSocketClientService is deliberately NOT registered. Nothing resolves it from the
		// container: the terminal factories below build their websocket client via
		// ActivatorUtilities.CreateInstance rather than sp.GetRequiredService<...>(). MS DI tracks
		// every transient IAsyncDisposable it resolves for the life of the scope that resolved it; in
		// WASM the root scope lives until page unload, so resolving through the container on every
		// RecreateAsync() would permanently root one already-disposed WebSocketClientService per
		// character switch. ActivatorUtilities constructs the object (resolving its own constructor
		// dependencies from the provider) without the container ever tracking the result, so recreated
		// clients are free to be collected once the facade drops its reference. The one component that
		// did inject the interface — the /websocket-test dev harness — is gone.
		// Both clients keep their resume point here, so a reload resumes their sessions.
		services.AddSingleton<TerminalResumeStore>();
		// The play terminal's saved log, on until the player turns it off.
		services.AddSingleton<TerminalLog>();
		// Logs a terminal back in when a reconnect could not resume its session.
		services.AddSingleton<ITerminalLoginTokens>(sp => new AccountTerminalLoginTokens(sp));
		services.AddSingleton(sp => new TerminalServiceHost(
			() =>
			{
				// The command terminal is the portal's background query connection — its OOB lookups must not
				// register a player as online, so it declares presence class "portal". The play terminal below
				// keeps the default "play".
				var ws = ActivatorUtilities.CreateInstance<WebSocketClientService>(sp);
				// Literal "portal" (not PresenceClasses.Portal): the browser bundle does not reference
				// SharpMUSH.Library — see the ProjectReference note in SharpMUSH.Client.csproj.
				ws.PresenceClass = "portal";
				return new TerminalService(ws, sp.GetRequiredService<ILogger<TerminalService>>(),
					sp.GetRequiredService<ITerminalLoginTokens>());
			}));
		services.AddSingleton<ITerminalService>(sp => sp.GetRequiredService<TerminalServiceHost>());

		// Second, independent connection for the /play page (player interactions), separate from the
		// command/softcode terminal above, held by the tab's character session with the feeds read off it:
		// channels and pages for the Play sidebar (the comm-feed package pushes to every connection a player
		// has, and only the play connection is the one to count) and the channel view's member list. The
		// server's history and read markers come through ICommHistory, which Program registers; without it the
		// feed counts on its own. Built by AttachPlayTerminalFeeds, not on first use — see there.
		// The session's parts stay resolvable on their own, so every @inject site and constructor keeps working.
		services.AddSingleton(CharacterSession.NewTerminal);
		services.AddSingleton<IPlayTerminalService>(sp => sp.GetRequiredService<PlayTerminalServiceHost>());
		services.AddSingleton(sp => new CharacterSessionRegistry(
			CharacterSession.Over(sp.GetRequiredService<PlayTerminalServiceHost>(), sp)));
		services.AddSingleton(sp => sp.GetRequiredService<CharacterSessionRegistry>().Focused);
		services.AddSingleton(sp => sp.GetRequiredService<CharacterSession>().Comms);
		services.AddSingleton(sp => sp.GetRequiredService<CharacterSession>().ChannelWho);

		services.AddSingleton<CharacterSwitchService>();
		services.AddSingleton<TerminalLoginService>();
		services.AddSingleton<ICharacterUpgradeService, CharacterUpgradeService>();

		return services;
	}

	/// <summary>
	/// Builds the feeds that read the play terminal's OOB store, so they are listening before the play
	/// terminal carries anything. The store keeps only the latest payload per package, so a
	/// <c>comm.message</c> that arrives before <see cref="ICommFeed"/> subscribes is lost, unread count and
	/// all — and a singleton factory builds nothing until something resolves it, which nothing on the way
	/// to connecting the play terminal (a login, <see cref="CharacterUpgradeService"/>) does. Call once,
	/// right after the host is built.
	/// </summary>
	public static void AttachPlayTerminalFeeds(this IServiceProvider services)
	{
		services.GetRequiredService<CharacterSessionRegistry>();
	}
}
