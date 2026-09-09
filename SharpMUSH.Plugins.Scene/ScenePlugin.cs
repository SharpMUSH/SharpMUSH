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
///   <item><see cref="IMigrationSource"/> contributes the provider-specific scene schema.</item>
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

	/// <summary>
	/// Maps the plugin-owned <c>SceneHub</c> at <c>/hubs/scene</c> into the host pipeline. The host invokes
	/// this after mapping its own controllers/hubs (see <c>Program.ConfigureApp</c>). The MVC controller is
	/// surfaced via the ApplicationPart in <see cref="RegisterServices"/>, so only the hub is mapped here.
	/// </summary>
	public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
		endpoints.MapHub<SceneHub>("/hubs/scene");

	// Deterministic membership writes must never run beside unconverted legacy edges.
	public bool RequireSuccessfulSurrealMigrations => true;

	/// <summary>
	/// SurrealDB scene-graph schema (tables + RELATE-edge tables + traversal indexes), moved out of
	/// <c>SurrealDatabase.Migration.cs</c>. The host runs each statement after its built-in batch.
	/// </summary>
	public IEnumerable<string> SurrealStatements =>
	[
		"DEFINE TABLE IF NOT EXISTS scene SCHEMALESS",
		"DEFINE TABLE IF NOT EXISTS scene_pose SCHEMALESS",
		"DEFINE TABLE IF NOT EXISTS scene_pose_edit SCHEMALESS",
		"DEFINE TABLE IF NOT EXISTS scene_plot SCHEMALESS",
		// One row per player, rewritten by every focus change so that two concurrent changes conflict
		// and retry instead of both committing under snapshot isolation.
		"DEFINE TABLE IF NOT EXISTS scene_focus SCHEMALESS",
		// Older versions reset these counters on every boot. Repair them from stored numeric IDs,
		// without lowering a counter whose higher IDs have since been deleted.
		"UPSERT counter:scene_id SET seq = math::max(array::concat([seq ?? 0, 0], " +
		"(SELECT VALUE <int> meta::id(id) FROM scene WHERE string::matches(<string> meta::id(id), '^[0-9]+$'))))",
		"UPSERT counter:pose_id SET seq = math::max(array::concat([seq ?? 0, 0], " +
		"(SELECT VALUE <int> meta::id(id) FROM scene_pose WHERE string::matches(<string> meta::id(id), '^[0-9]+$'))))",
		"DEFINE INDEX IF NOT EXISTS scene_status ON scene FIELDS status",
		"DEFINE INDEX IF NOT EXISTS scene_scheduledfor ON scene FIELDS scheduledFor",
		"DEFINE INDEX IF NOT EXISTS scene_public ON scene FIELDS isPublic",
		"DEFINE INDEX IF NOT EXISTS scene_lastactivity ON scene FIELDS lastActivityAt",
		"DEFINE TABLE IF NOT EXISTS scene_first_pose TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_last_pose TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_pose_next TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_pose_in_scene TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_first_edit TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_current_edit TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_next_edit TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_plot_includes TYPE RELATION",
		// Object edges into the game-object graph (incarnation-safe; *Name snapshot kept on the vertex).
		"DEFINE TABLE IF NOT EXISTS scene_in_room TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_owner TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_starter TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_pose_author TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_pose_origin TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_edit_editor TYPE RELATION",
		"DEFINE TABLE IF NOT EXISTS scene_plot_owner TYPE RELATION",
		// The member edge (player -> scene) carries {role, showAs, isCurrent, grantedAt, memberName}.
		"DEFINE TABLE IF NOT EXISTS scene_member TYPE RELATION",
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
		"DEFINE INDEX IF NOT EXISTS scene_member_out ON scene_member FIELDS out",
		// Convert legacy random ids before any membership writes. Preserve conflicting duplicate
		// rows in an ordinary backup table, and retain the earliest nonempty role/persona/name and
		// any active focus. The marker and graph changes commit together, once per database.
		"""
		BEGIN TRANSACTION;
		IF !record::exists(migration:scene_member_ids_v1) {
			LET $multiple_focus = SELECT in, count() AS total FROM scene_member WHERE isCurrent = true GROUP BY in;
			LET $groups = SELECT in, out FROM scene_member GROUP BY in, out;
			FOR $pair IN $groups {
				LET $members = SELECT * FROM scene_member
					WHERE in = $pair.in AND out = $pair.out ORDER BY grantedAt, id;
				LET $keep = $members[0];
				FOR $member IN $members {
					IF array::len($members) > 1 OR $multiple_focus[WHERE in = $pair.in][0].total > 1 {
						CREATE scene_member_duplicate_backup CONTENT { original: $member };
					};
					DELETE $member.id;
				};
				INSERT RELATION INTO scene_member {
					id: type::thing('scene_member', [$pair.in, $pair.out]),
					in: $pair.in, out: $pair.out,
					role: $members[WHERE role != NONE AND role != ''][0].role ?? '',
					showAs: $members[WHERE showAs != NONE AND showAs != ''][0].showAs ?? '',
					isCurrent: array::len($members[WHERE isCurrent = true]) > 0,
					memberName: $members[WHERE memberName != NONE AND memberName != ''][0].memberName ?? '',
					grantedAt: $keep.grantedAt
				};
			};
			LET $players = SELECT in FROM scene_member WHERE isCurrent = true GROUP BY in;
			FOR $player IN $players {
				LET $focused = SELECT id, grantedAt FROM scene_member
					WHERE in = $player.in AND isCurrent = true ORDER BY grantedAt, id;
				FOR $edge IN $focused {
					IF $edge.id != $focused[0].id { UPDATE $edge.id SET isCurrent = false; };
				};
			};
			CREATE migration:scene_member_ids_v1 SET appliedAt = time::now();
		};
		COMMIT TRANSACTION;
		"""
	];

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
