using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
				return new TerminalService(ws, sp.GetRequiredService<ILogger<TerminalService>>());
			}));
		services.AddSingleton<ITerminalService>(sp => sp.GetRequiredService<TerminalServiceHost>());

		// Second, independent connection for the /play page (player interactions), separate from the
		// command/softcode terminal above. Both are singletons so each survives navigation.
		services.AddSingleton(sp => new PlayTerminalServiceHost(
			() => new PlayTerminalService(
				ActivatorUtilities.CreateInstance<PlayWebSocketClientService>(sp),
				sp.GetRequiredService<ILogger<TerminalService>>())));
		services.AddSingleton<IPlayTerminalService>(sp => sp.GetRequiredService<PlayTerminalServiceHost>());

		services.AddSingleton<CharacterSwitchService>();
		services.AddSingleton<TerminalLoginService>();
		services.AddSingleton<ICharacterUpgradeService, CharacterUpgradeService>();

		return services;
	}
}
