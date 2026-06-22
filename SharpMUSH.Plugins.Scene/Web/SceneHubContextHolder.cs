using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// Bridges DI to the plugin's <see cref="SharpMUSH.Library.Plugins.IBridgeSubscriptionSource"/> leg. The
/// plugin entry type (<c>ScenePlugin</c>) is instantiated by the loader via <c>Activator.CreateInstance</c> —
/// NOT from the host container — so its bridge <c>RunAsync</c> cannot constructor-inject the scene hub
/// context. This hosted singleton IS resolved from DI; the host constructs it during startup (it is
/// registered as an <see cref="IHostedService"/>), publishing the (non-generic) <c>IHubContext&lt;SceneHub&gt;</c>
/// to a static slot the plugin's bridge leg reads.
///
/// <para>The hub context is the NON-generic <c>IHubContext&lt;SceneHub&gt;</c> (not the strongly-typed
/// <c>IHubContext&lt;SceneHub, TClient&gt;</c>): the strongly-typed surface uses Reflection.Emit which cannot
/// reference the collectible plugin ALC. Forwarding goes through
/// <c>SendAsync("ReceiveSceneMessage", …)</c>.</para>
/// </summary>
public sealed class SceneHubContextHolder : IHostedService
{
	/// <summary>The plugin's scene hub context, set once this singleton is constructed.</summary>
	public static IHubContext<SceneHub>? HubContext { get; private set; }

	public SceneHubContextHolder(IHubContext<SceneHub> hubContext) =>
		HubContext = hubContext;

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
