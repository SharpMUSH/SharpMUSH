using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Plugins.Scene.Storage;
using SharpMUSH.Plugins.Scene.Web;

namespace SharpMUSH.Plugins.Scene;

/// <summary>
/// The Scene System as the Phase-5 reference plugin. This single <see cref="SharpPluginAttribute"/>
/// entry type carries every Scene seam out of the engine via the Phase-1/2a plugin contracts:
/// <list type="bullet">
///   <item><see cref="PluginBase"/> (→ <c>ICommandSource</c>/<c>IFunctionSource</c>) surfaces the
///   <c>@SCENE</c> command and the <c>scene…</c> functions, discovered the same way as in-tree code
///   through the generator analyzer this assembly references.</item>
///   <item><see cref="IFlagSource"/> contributes the informational <c>SCENE_ROOM</c> object flag.</item>
///   <item><see cref="IBridgeSubscriptionSource"/> contributes the <c>game.scene.*</c> NATS→SignalR leg,
///   forwarding to the plugin-owned <c>SceneHub</c> (Phase 9).</item>
///   <item><see cref="IEndpointContributor"/> (Phase 9) maps the plugin's own <c>SceneHub</c> at
///   <c>/hubs/scene</c> into the host pipeline.</item>
/// </list>
/// Phase 8 additionally makes it an <see cref="IServiceRegistrar"/>: it registers the Scene storage
/// (relocated out of the core DB providers into this plugin's <c>Storage/</c>) via
/// <c>services.AddSceneSystem()</c>, over the host-shared Lightning storage accessor.
/// Phase 9 also moves the scene REST controller and the scene SignalR hub into this plugin: the controller
/// is registered as an MVC ApplicationPart and the hub is mapped via <see cref="IEndpointContributor"/>.
/// Because it contributes load-once state (a flag, a long-lived bridge subscription, DI
/// registration, and mapped endpoints) it is deliberately a <b>non-unloadable</b> plugin:
/// <c>PluginLoaderService.IsUnloadablePlugin</c> returns false for any plugin implementing
/// <see cref="IServiceRegistrar"/>/<see cref="IMigrationSource"/>/<see cref="IFlagSource"/>/
/// <see cref="IBridgeSubscriptionSource"/>/<see cref="IEndpointContributor"/>.
/// </summary>
[SharpPlugin]
public sealed class ScenePlugin
	: PluginBase, IServiceRegistrar, IFlagSource, IBridgeSubscriptionSource, IEndpointContributor
{
	public override string Id => "scene";
	public override string Version => "1.0.0";

	/// <summary>
	/// Registers the Scene System's storage (this plugin owns it as of Phase 8) into the host container:
	/// the <see cref="ISceneStorage"/> over the host-shared Lightning storage accessor, plus the
	/// <c>ISceneService</c> composed with any registered behaviors. No behaviors ship by default.
	/// </summary>
	public void RegisterServices(IServiceCollection services)
	{
		services.AddSceneSystem();

		// Phase 9 — the scene REST controller now lives in THIS plugin assembly. Register it as an MVC
		// ApplicationPart so the host's controller discovery finds it across the plugin ALC; the route
		// (api/scenes) is unchanged. AddControllers()/AddApplicationPart are idempotent — the host also
		// calls AddControllers, and adding the same part twice is de-duplicated by the MVC part manager.
		services.AddControllers().AddApplicationPart(typeof(ScenePlugin).Assembly);

		// Phase 9 — the scene realtime hub (SceneHub) is mapped by this plugin (see MapEndpoints). SignalR
		// must be registered; AddSignalR is idempotent, so it is safe alongside the host's own AddSignalR.
		services.AddSignalR();

		// Bridge DI→plugin: a hosted singleton that, when the host constructs it at startup, captures the
		// strongly-typed IHubContext<SceneHub, ISceneHubClient> into a static slot the bridge leg reads
		// (the plugin entry type is Activator-constructed, not DI-constructed, so it cannot inject it).
		services.AddHostedService<SceneHubContextHolder>();
	}

	/// <summary>
	/// Maps the plugin-owned <c>SceneHub</c> at <c>/hubs/scene</c> into the host pipeline. The host invokes
	/// this after mapping its own controllers/hubs (see <c>Program.ConfigureApp</c>). The MVC controller is
	/// surfaced via the ApplicationPart in <see cref="RegisterServices"/>, so only the hub is mapped here.
	/// </summary>
	public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
		endpoints.MapHub<SceneHub>("/hubs/scene");

	/// <summary>
	/// The informational <c>SCENE_ROOM</c> object flag (symbol <c>S</c>, room-only, wizard set/unset),
	/// kept out of the provider's built-in flag seed. Idempotent: the host UPSERTs/MERGEs by flag name.
	/// </summary>
	public IEnumerable<PluginFlag> Flags =>
	[
		new PluginFlag(
			Name: "SCENE_ROOM",
			Symbol: "S",
			Aliases: [],
			SetPermissions: ["wizard"],
			UnsetPermissions: ["wizard"],
			TypeRestrictions: ["ROOM"])
	];

	/// <summary>
	/// The <c>game.scene.*</c> NATS→SignalR realtime leg. Subscribes to the transient core-NATS subject and
	/// forwards each <see cref="SceneEventMessage"/> to the plugin-owned <c>SceneHub</c>'s <c>scene:{id}</c>
	/// group. Phase 9: the scene hub now lives in this plugin, so the leg forwards through the strongly-typed
	/// <c>IHubContext&lt;SceneHub, ISceneHubClient&gt;</c> (captured from DI by <see cref="SceneHubContextHolder"/>),
	/// NOT the host's <c>GameHub</c> context the bridge passes in (which targets a different hub). The
	/// <paramref name="hubContext"/> parameter is therefore ignored.
	/// </summary>
	public async Task RunAsync(object natsConnection, object hubContext, CancellationToken ct)
	{
		var nats = (NatsConnection)natsConnection;

		// Subject wildcard: "game.scene.*" — the last token is the scene id.
		await foreach (var msg in nats.SubscribeAsync<SceneEventMessage>(
			"game.scene.*",
			serializer: NatsJsonSerializer<SceneEventMessage>.Default,
			cancellationToken: ct))
		{
			if (msg.Data is null) continue;

			// The holder is constructed at host startup; if the bridge somehow runs before it resolved, skip
			// (the next message after startup forwards normally). Non-generic IHubContext + SendAsync so no
			// Reflection.Emit proxy crosses the collectible plugin ALC.
			var hub = SceneHubContextHolder.HubContext;
			if (hub is null) continue;

			var group = SceneHub.SceneGroupName(msg.Data.SceneId);
			try
			{
				await hub.Clients.Group(group).SendAsync(SceneHub.ReceiveSceneMessageMethod, msg.Data, ct);
			}
			catch (Exception) when (!ct.IsCancellationRequested)
			{
				// Best-effort live feed: a forwarding error on one message must not tear down the loop.
			}
		}
	}
}
