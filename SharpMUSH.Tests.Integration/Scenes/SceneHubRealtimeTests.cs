using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Phase 9 realtime wiring proof for the plugin-owned scene hub (mapped at <c>/hubs/scene</c> by the Scene
/// plugin's <c>IEndpointContributor</c>). The Scene plugin is loaded at runtime, not linked, so its
/// <c>SceneHub</c>/<c>SceneHubContextHolder</c> types are reached by reflection (as in the Phase 8 boundary
/// tests). These assertions prove, against the running server:
///
/// <list type="bullet">
///   <item>the plugin's <c>IEndpointContributor.MapEndpoints</c> ran in the real host pipeline — a
///   <c>/hubs/scene</c> SignalR endpoint is mapped;</item>
///   <item>the non-generic <c>IHubContext&lt;SceneHub&gt;</c> resolves from host DI across the collectible
///   plugin ALC (no Reflection.Emit proxy needed — the hub is a plain <c>Hub</c>), so the plugin's bridge
///   leg has a live forwarding target;</item>
///   <item>the hosted <c>SceneHubContextHolder</c> published that hub context into the static slot the
///   bridge leg reads, i.e. the NATS <c>game.scene.{id}</c> → SignalR <c>scene:{id}</c> leg is fully wired.</item>
/// </list>
/// </summary>
[NotInParallel]
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SceneHubRealtimeTests(ServerWebAppFactory factory)
{
	private static Type SceneHubType =>
		AppDomain.CurrentDomain.GetAssemblies()
			.First(a => a.GetName().Name == "SharpMUSH.Plugins.Scene")
			.GetType("SharpMUSH.Plugins.Scene.Web.SceneHub", throwOnError: true)!;

	private static Type SceneHubContextHolderType =>
		AppDomain.CurrentDomain.GetAssemblies()
			.First(a => a.GetName().Name == "SharpMUSH.Plugins.Scene")
			.GetType("SharpMUSH.Plugins.Scene.Web.SceneHubContextHolder", throwOnError: true)!;

	[Test]
	public async Task SceneHub_IsMappedAt_HubsScene()
	{
		var routes = factory.Services.GetServices<EndpointDataSource>()
			.SelectMany(s => s.Endpoints)
			.OfType<RouteEndpoint>()
			.Select(e => e.RoutePattern.RawText)
			.ToList();

		await Assert.That(routes.Any(r => r is not null && r.StartsWith("/hubs/scene", StringComparison.Ordinal)))
			.IsTrue()
			.Because("the Scene plugin's IEndpointContributor must have mapped SceneHub at /hubs/scene");
	}

	[Test]
	public async Task SceneHubContext_ResolvesAcrossPluginAlc_AndBridgeTargetIsPublished()
	{
		// IHubContext<SceneHub> resolves — the plugin hub type unified with host SignalR across the
		// collectible ALC, and a plain Hub avoided the cross-ALC Reflection.Emit proxy.
		var hubContextType = typeof(Microsoft.AspNetCore.SignalR.IHubContext<>).MakeGenericType(SceneHubType);
		var hubContext = factory.Services.GetService(hubContextType);
		await Assert.That(hubContext).IsNotNull()
			.Because("IHubContext<SceneHub> must resolve from host DI across the plugin ALC");

		// The hosted SceneHubContextHolder ran at startup and published the bridge's forwarding target.
		var holderHubContext = SceneHubContextHolderType
			.GetProperty("HubContext", BindingFlags.Public | BindingFlags.Static)!
			.GetValue(null);
		await Assert.That(holderHubContext).IsNotNull()
			.Because("the hosted SceneHubContextHolder must publish the hub context the bridge leg forwards through");
	}
}
