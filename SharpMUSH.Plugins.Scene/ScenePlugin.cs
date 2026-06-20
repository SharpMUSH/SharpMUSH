using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Plugins;

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
///   <item><see cref="IBridgeSubscriptionSource"/> contributes the <c>game.scene.*</c> NATS→SignalR leg.</item>
/// </list>
/// Because it contributes load-once state (a migration, a flag, and a long-lived bridge subscription) it is
/// deliberately a <b>non-unloadable</b> plugin: <c>PluginLoaderService.IsUnloadablePlugin</c> returns false
/// for any plugin implementing <see cref="IMigrationSource"/>/<see cref="IFlagSource"/>/
/// <see cref="IBridgeSubscriptionSource"/>.
/// </summary>
[SharpPlugin]
public sealed class ScenePlugin
	: PluginBase, IMigrationSource, IFlagSource, IBridgeSubscriptionSource
{
	public override string Id => "scene";
	public override string Version => "1.0.0";

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
	/// The <c>game.scene.*</c> NATS→SignalR realtime leg, moved out of <c>NatsBridgeService</c>. Subscribes
	/// to the transient core-NATS subject and forwards each <see cref="SceneEventMessage"/> to the SignalR
	/// <c>scene:{id}</c> group. The host passes the concrete <c>NatsConnection</c> and (non-generic)
	/// <c>IHubContext</c>; forwarding through the non-generic hub context + <c>SendAsync("ReceiveSceneMessage")</c>
	/// keeps this leg free of any reference to the Server's <c>GameHub</c>/<c>IGameHubClient</c> types.
	/// </summary>
	public async Task RunAsync(object natsConnection, object hubContext, CancellationToken ct)
	{
		var nats = (NatsConnection)natsConnection;
		var hub = (IHubContext)hubContext;

		// Subject wildcard: "game.scene.*" — the last token is the scene id.
		await foreach (var msg in nats.SubscribeAsync<SceneEventMessage>(
			"game.scene.*",
			serializer: NatsJsonSerializer<SceneEventMessage>.Default,
			cancellationToken: ct))
		{
			if (msg.Data is null) continue;

			var group = SceneGroupName(msg.Data.SceneId);
			try
			{
				await hub.Clients.Group(group).SendAsync("ReceiveSceneMessage", msg.Data, ct);
			}
			catch (Exception) when (!ct.IsCancellationRequested)
			{
				// Best-effort live feed: a forwarding error on one message must not tear down the loop.
			}
		}
	}

	/// <summary>The SignalR scene group key — mirrors <c>GameHub.SceneGroupName</c> (<c>scene:{id}</c>).</summary>
	private static string SceneGroupName(string sceneId) => $"scene:{sceneId}";
}
