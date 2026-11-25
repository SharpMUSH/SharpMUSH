using Microsoft.AspNetCore.Components;
using SharpMUSH.Client.Services.Play;
using SharpMUSH.Library.Models.Portal.Play;

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
		// The play terminal's longer log, when the player turns it on.
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
		// command/softcode terminal above. Both are singletons so each survives navigation.
		services.AddSingleton(sp => new PlayTerminalServiceHost(
			() => new PlayTerminalService(
				ActivatorUtilities.CreateInstance<PlayWebSocketClientService>(sp),
				sp.GetRequiredService<ILogger<TerminalService>>(),
				sp.GetRequiredService<ITerminalLoginTokens>(),
				sp.GetRequiredService<TerminalLog>())));
		services.AddSingleton<IPlayTerminalService>(sp => sp.GetRequiredService<PlayTerminalServiceHost>());

		// Channels and pages for the Play sidebar, read off the play terminal's OOB store: the comm-feed
		// package pushes to every connection a player has, and only the play connection is the one to
		// count. Built by AttachPlayTerminalFeeds, not on first use — see there. The server's history and read
		// markers come through ICommHistory, which Program registers; without it the feed counts on its own.
		// The play connection too: a reconnect logs in again without replaying what was missed, so the feed pulls
		// it from the server.
		services.AddSingleton<ICommFeed>(sp => new OobCommFeed(sp.GetRequiredService<PlayTerminalServiceHost>().OobChannels,
			history: sp.GetService<ICommHistory>(), connection: sp.GetRequiredService<PlayTerminalServiceHost>()));
		// The channel view's member list, off the same store: comm.who pushes keep the list it reads current.
		services.AddSingleton<IChannelWho>(sp => new ChannelWhoFeed(sp.GetRequiredService<PlayTerminalServiceHost>().OobChannels,
			sp.GetService<ICommHistory>()));

		// The play connection as an event stream, for applications. Not registered as IPlaySession: an application
		// gets it only through its page, limited to the packages it declares. Built by AttachPlayTerminalFeeds so it sees
		// every package from the first. It is also an IAccountSessionEndingHandler, which Program registers ahead
		// of the terminals' teardown, so it resets for the sign-out before the terminals hang up. The account
		// service is resolved when used: it takes every session-ending handler, this one included.
		services.AddSingleton(sp => new PlaySession(sp.GetRequiredService<PlayTerminalServiceHost>(),
			() => ConnectPlayAsync(sp),
			() => sp.GetRequiredService<AccountAuthService>().Username is not null,
			sp.GetRequiredService<ILogger<PlaySession>>()));

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
		services.GetRequiredService<ICommFeed>();
		services.GetRequiredService<IChannelWho>();
		services.GetRequiredService<PlaySession>();
	}

	/// <summary>
	/// Connects the play terminal as the tab's active character, else the account's first, as the Play page's
	/// Connect does.
	/// </summary>
	private static async Task ConnectPlayAsync(IServiceProvider services)
	{
		var accountAuth = services.GetRequiredService<AccountAuthService>();
		var terminal = services.GetRequiredService<PlayTerminalServiceHost>();
		if ((accountAuth.ActiveCharacter ?? accountAuth.Characters.FirstOrDefault()) is not { } character) return;
		if (await accountAuth.GetOttForCharacterAsync(character) is not string ott) return;

		accountAuth.SetActiveCharacter(character);
		terminal.ConnectedPlayerName = character.Name;
		await terminal.ConnectWithOttAsync(TerminalEndpoint.Resolve(services.GetRequiredService<NavigationManager>().BaseUri),
			ott, TerminalIdentity.Of(accountAuth.Username, character));
	}
}
