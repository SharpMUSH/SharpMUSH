using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
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
///   <item><see cref="IMigrationSource"/> contributes the Arango <c>Migration_AddScenes</c> assembly and
///   the Memgraph/Surreal scene schema statements.</item>
///   <item><see cref="IFlagSource"/> contributes the informational <c>SCENE_ROOM</c> object flag.</item>
///   <item><see cref="IBridgeSubscriptionSource"/> contributes the <c>game.scene.*</c> NATS→SignalR leg,
///   forwarding to the plugin-owned <c>SceneHub</c> (Phase 9).</item>
///   <item><see cref="IEndpointContributor"/> (Phase 9) maps the plugin's own <c>SceneHub</c> at
///   <c>/hubs/scene</c> into the host pipeline.</item>
/// </list>
/// Phase 8 additionally makes it an <see cref="IServiceRegistrar"/>: it registers the Scene storage
/// (relocated out of the core DB providers into this plugin's <c>Storage/</c>) via
/// <c>services.AddSceneSystem(configuration)</c>, keyed per provider over the host-shared storage accessors.
/// Phase 9 also moves the scene REST controller and the scene SignalR hub into this plugin: the controller
/// is registered as an MVC ApplicationPart and the hub is mapped via <see cref="IEndpointContributor"/>.
/// Because it contributes load-once state (a migration, a flag, a long-lived bridge subscription, DI
/// registration, and mapped endpoints) it is deliberately a <b>non-unloadable</b> plugin:
/// <c>PluginLoaderService.IsUnloadablePlugin</c> returns false for any plugin implementing
/// <see cref="IServiceRegistrar"/>/<see cref="IMigrationSource"/>/<see cref="IFlagSource"/>/
/// <see cref="IBridgeSubscriptionSource"/>/<see cref="IEndpointContributor"/>.
/// </summary>
[SharpPlugin]
public sealed class ScenePlugin
	: PluginBase, IServiceRegistrar, IMigrationSource, IFlagSource, IBridgeSubscriptionSource, IEndpointContributor
{
	public override string Id => "scene";
	public override string Version => "1.0.0";

	// ── IServiceRegistrar ─────────────────────────────────────────────────────────

	/// <summary>
	/// Registers the Scene System's storage (this plugin owns it as of Phase 8) into the host container:
	/// keyed per-provider <see cref="ISceneStorage"/> over the host-shared storage accessors, plus the
	/// active-provider <c>ISceneService</c> composed with any registered behaviors. No behaviors ship by
	/// default. An empty configuration is passed as a fallback; the factory prefers the host's real
	/// <see cref="IConfiguration"/> resolved from the container at runtime.
	/// </summary>
	public void RegisterServices(IServiceCollection services)
	{
		// Scene storage (Phase 8): keyed per-provider ISceneStorage + the active-provider ISceneService.
		services.AddSceneSystem(new ConfigurationBuilder().Build());

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

	// ── IEndpointContributor (Phase 9) ──────────────────────────────────────────────

	/// <summary>
	/// Maps the plugin-owned <c>SceneHub</c> at <c>/hubs/scene</c> into the host pipeline. The host invokes
	/// this after mapping its own controllers/hubs (see <c>Program.ConfigureApp</c>). The MVC controller is
	/// surfaced via the ApplicationPart in <see cref="RegisterServices"/>, so only the hub is mapped here.
	/// </summary>
	public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
		endpoints.MapHub<SceneHub>("/hubs/scene");

	// ── IMigrationSource ──────────────────────────────────────────────────────────

	/// <summary>The plugin's own assembly carries <c>Migration_AddScenes</c> (an <c>IArangoMigration</c>);
	/// the host's <c>ArangoDatabase.Migrate()</c> feeds this to <c>migrator.AddMigrations(...)</c>.</summary>
	public Assembly? ArangoMigrationAssembly => typeof(ScenePlugin).Assembly;

	/// <summary>
	/// Memgraph scene-graph schema (indexes + uniqueness constraints), moved out of
	/// <c>MemgraphDatabase.Migration.cs</c>. The host runs each statement after its built-in batch, each
	/// isolated so an "already exists" failure does not abort the rest.
	/// </summary>
	public IEnumerable<string> CypherStatements =>
	[
		"CREATE INDEX ON :SharpScene(sceneId)",
		"CREATE INDEX ON :SharpScene(status)",
		"CREATE INDEX ON :SharpScene(isPublic)",
		"CREATE INDEX ON :SharpScene(scheduledFor)",
		"CREATE INDEX ON :SharpScene(lastActivityAt)",
		"CREATE CONSTRAINT ON (s:SharpScene) ASSERT s.sceneId IS UNIQUE",
		"CREATE INDEX ON :SharpScenePose(poseId)",
		"CREATE INDEX ON :SharpScenePose(createdAt)",
		"CREATE CONSTRAINT ON (p:SharpScenePose) ASSERT p.poseId IS UNIQUE",
		"CREATE INDEX ON :SharpScenePoseEdit(editId)",
		"CREATE CONSTRAINT ON (e:SharpScenePoseEdit) ASSERT e.editId IS UNIQUE",
		"CREATE INDEX ON :SharpScenePlot(plotId)",
		"CREATE CONSTRAINT ON (pl:SharpScenePlot) ASSERT pl.plotId IS UNIQUE"
	];

	/// <summary>
	/// SurrealDB scene-graph schema (tables + RELATE-edge tables + traversal indexes), moved out of
	/// <c>SurrealDatabase.Migration.cs</c>. The host runs each statement after its built-in batch.
	/// </summary>
	public IEnumerable<string> SurrealStatements =>
	[
		// Vertices.
		"DEFINE TABLE scene SCHEMALESS",
		"DEFINE TABLE scene_pose SCHEMALESS",
		"DEFINE TABLE scene_pose_edit SCHEMALESS",
		"DEFINE TABLE scene_plot SCHEMALESS",
		// 1-based scene/pose id counters seeded at 0 (runtime UPDATE increments atomically). Phase 9 moved
		// this seed out of the core SurrealDB migration into the plugin alongside the scene schema.
		"UPSERT counter:scene_id SET seq = 0",
		"UPSERT counter:pose_id SET seq = 0",
		// Scene first-class field indexes (status, scheduling, visibility, recency).
		"DEFINE INDEX IF NOT EXISTS scene_status ON scene FIELDS status",
		"DEFINE INDEX IF NOT EXISTS scene_scheduledfor ON scene FIELDS scheduledFor",
		"DEFINE INDEX IF NOT EXISTS scene_public ON scene FIELDS isPublic",
		"DEFINE INDEX IF NOT EXISTS scene_lastactivity ON scene FIELDS lastActivityAt",
		// Structural edges (within the scene graph).
		"DEFINE TABLE scene_first_pose TYPE RELATION",
		"DEFINE TABLE scene_last_pose TYPE RELATION",
		"DEFINE TABLE scene_pose_next TYPE RELATION",
		"DEFINE TABLE scene_pose_in_scene TYPE RELATION",
		"DEFINE TABLE scene_first_edit TYPE RELATION",
		"DEFINE TABLE scene_current_edit TYPE RELATION",
		"DEFINE TABLE scene_next_edit TYPE RELATION",
		"DEFINE TABLE scene_plot_includes TYPE RELATION",
		// Object edges into the game-object graph (incarnation-safe; *Name snapshot kept on the vertex).
		"DEFINE TABLE scene_in_room TYPE RELATION",
		"DEFINE TABLE scene_owner TYPE RELATION",
		"DEFINE TABLE scene_starter TYPE RELATION",
		"DEFINE TABLE scene_pose_author TYPE RELATION",
		"DEFINE TABLE scene_pose_origin TYPE RELATION",
		"DEFINE TABLE scene_edit_editor TYPE RELATION",
		"DEFINE TABLE scene_plot_owner TYPE RELATION",
		// The member edge (player -> scene) carries {role, showAs, isCurrent, grantedAt, memberName}.
		"DEFINE TABLE scene_member TYPE RELATION",
		// Edge traversal indexes.
		"DEFINE INDEX IF NOT EXISTS scene_first_pose_in ON scene_first_pose FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_last_pose_in ON scene_last_pose FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_pose_next_in ON scene_pose_next FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_pose_next_out ON scene_pose_next FIELDS out",
		"DEFINE INDEX IF NOT EXISTS scene_pose_in_scene_in ON scene_pose_in_scene FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_pose_in_scene_out ON scene_pose_in_scene FIELDS out",
		"DEFINE INDEX IF NOT EXISTS scene_first_edit_in ON scene_first_edit FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_current_edit_in ON scene_current_edit FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_next_edit_in ON scene_next_edit FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_plot_includes_in ON scene_plot_includes FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_plot_includes_out ON scene_plot_includes FIELDS out",
		"DEFINE INDEX IF NOT EXISTS scene_in_room_in ON scene_in_room FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_in_room_out ON scene_in_room FIELDS out",
		"DEFINE INDEX IF NOT EXISTS scene_owner_in ON scene_owner FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_starter_in ON scene_starter FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_pose_author_in ON scene_pose_author FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_pose_origin_in ON scene_pose_origin FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_edit_editor_in ON scene_edit_editor FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_plot_owner_in ON scene_plot_owner FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_member_in ON scene_member FIELDS in",
		"DEFINE INDEX IF NOT EXISTS scene_member_out ON scene_member FIELDS out"
	];

	// ── IFlagSource ───────────────────────────────────────────────────────────────

	/// <summary>
	/// The informational <c>SCENE_ROOM</c> object flag (symbol <c>S</c>, room-only, wizard set/unset),
	/// moved out of every provider's built-in flag seed. Idempotent: the host UPSERTs/MERGEs by flag name.
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

	// ── IBridgeSubscriptionSource ───────────────────────────────────────────────────

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
