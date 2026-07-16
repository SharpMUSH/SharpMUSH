using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Registers the terminal facades (Task 6) as DI singletons. Extracted from <c>Program.cs</c> into a
/// real production method so the registration shape itself can be exercised by tests directly, rather
/// than re-typed inside a test fixture where it could silently drift from what actually runs.
/// </summary>
public static class TerminalServiceCollectionExtensions
{
	/// <summary>
	/// Registers both terminal connections as stable facades (Task 6). Each holds a swappable inner
	/// terminal so a character switch can dispose and rebuild the connection — every <c>@inject</c>
	/// site and <see cref="MushQueryService"/>'s constructor capture keep pointing at the facade, which
	/// never changes identity. The concrete facade type is also registered directly (aliased to the
	/// same instance) so the character-switch flow can call <c>RecreateAsync()</c> without casting from
	/// the interface.
	/// </summary>
	public static IServiceCollection AddTerminalServices(this IServiceCollection services)
	{
		// The transient interface registrations are kept for Pages/WebSocketTest.razor, which injects
		// IWebSocketClientService / IPlayWebSocketClientService directly as a standalone diagnostic
		// tool independent of the terminal facades below.
		//
		// The terminal factories deliberately do NOT resolve through those registrations — they build
		// the websocket client via ActivatorUtilities.CreateInstance instead of
		// sp.GetRequiredService<...>(). MS DI tracks every transient IAsyncDisposable it resolves for
		// the life of the scope that resolved it; in WASM the root scope lives until page unload, so
		// resolving through the container on every RecreateAsync() would permanently root one
		// already-disposed WebSocketClientService per character switch. ActivatorUtilities constructs
		// the object (resolving its own constructor dependencies from the provider) without the
		// container ever tracking the result, so recreated clients are free to be collected once the
		// facade drops its reference.
		services.AddTransient<IWebSocketClientService, WebSocketClientService>();
		services.AddSingleton(sp => new TerminalServiceHost(
			() => new TerminalService(
				ActivatorUtilities.CreateInstance<WebSocketClientService>(sp),
				sp.GetRequiredService<ILogger<TerminalService>>())));
		services.AddSingleton<ITerminalService>(sp => sp.GetRequiredService<TerminalServiceHost>());

		// Second, independent connection for the /play page (player interactions), separate from the
		// command/softcode terminal above. Both are singletons so each survives navigation.
		services.AddTransient<IPlayWebSocketClientService, PlayWebSocketClientService>();
		services.AddSingleton(sp => new PlayTerminalServiceHost(
			() => new PlayTerminalService(
				ActivatorUtilities.CreateInstance<PlayWebSocketClientService>(sp),
				sp.GetRequiredService<ILogger<TerminalService>>())));
		services.AddSingleton<IPlayTerminalService>(sp => sp.GetRequiredService<PlayTerminalServiceHost>());

		return services;
	}
}
