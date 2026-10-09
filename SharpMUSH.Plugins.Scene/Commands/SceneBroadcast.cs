using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Publishes the realtime <see cref="SceneEventMessage"/> on the core-NATS subject
/// <c>game.scene.{id}</c> — the wire the Scene plugin's bridge subscription
/// (<c>game.scene.*</c>, see <see cref="ScenePlugin"/>) forwards to the SignalR
/// <c>scene:{id}</c> group. This mirrors the <c>game.room.*</c> realtime leg:
/// publishing on a transient, targeted core-NATS subject (not the JetStream
/// <c>{prefix}.{kebab}</c> queue), so the publish stays on the same wire.
///
/// Publish lives outside <c>ISceneService</c> (which must remain provider-agnostic
/// and extractable): the <c>@SCENE</c> arms call this shared helper after a
/// successful pose write, so both the command and side-effect-function paths
/// broadcast the same stored pose, keyed by pose id.
/// </summary>
public static class SceneBroadcast
{
	/// <summary>Core-NATS subject prefix the bridge subscribes to as <c>game.scene.*</c>.</summary>
	private const string SceneSubjectPrefixToken = "game.scene";

	/// <summary>
	/// One cached core-NATS connection per URL. The bridge's subscription is core NATS
	/// (transient targeted delivery), so we publish core NATS too — never JetStream.
	/// </summary>
	private static readonly Dictionary<string, NatsConnection> Connections = new();
	private static readonly SemaphoreSlim ConnectionsLock = new(1, 1);

	/// <summary>
	/// Builds the realtime subject for a scene. Centralised so the subject is never
	/// hand-built at the call sites — mirrors <c>GameHub.SceneGroupName</c>.
	/// </summary>
	public static string SubjectForScene(string sceneId) => $"{SceneSubjectPrefixToken}.{sceneId}";

	/// <summary>
	/// Publishes a <see cref="SceneEventMessage"/> built from <paramref name="pose"/>
	/// to <c>game.scene.{sceneId}</c>. Call after a SUCCESSFUL pose mutation. Best-effort: a messaging
	/// failure is logged, never thrown, so it cannot turn a committed mutation into an error.
	/// </summary>
	/// <param name="parser">The active parser (source of the DI <see cref="IServiceProvider"/>).</param>
	/// <param name="sceneId">The owning scene id (the SignalR group key).</param>
	/// <param name="eventType">Opaque event kind: "pose" | "edit" | "delete" | "move".</param>
	/// <param name="pose">The resulting pose, or null when no pose payload is available.</param>
	public static async ValueTask PublishSceneEventAsync(
		IMUSHCodeParser parser,
		string sceneId,
		string eventType,
		ScenePose? pose)
	{
		var options = parser.ServiceProvider.GetService<NatsOptions>();
		if (options is null)
		{
			// No messaging configured (e.g. a parser instance without the NATS bus) —
			// the mutation already succeeded; the live feed is best-effort.
			return;
		}

		// Every caller has already committed its mutation. A failure here must not reach it: the softcode
		// caller would get no pose id for a pose that was recorded, and a retry would record it twice.
		try
		{
			var message = BuildMessage(sceneId, eventType, pose,
				await SceneLocate.ObjIdAsync(parser, pose?.AuthorDbref));

			var nats = await GetConnectionAsync(options.Url);
			await nats.PublishAsync(
				SubjectForScene(sceneId),
				message,
				serializer: NatsJsonSerializer<SceneEventMessage>.Default);
		}
		catch (Exception ex)
		{
			parser.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SceneBroadcast))
				.LogWarning(ex, "Scene {SceneId}: the {EventType} event was not published to the live feed", sceneId, eventType);
		}
	}

	/// <summary>
	/// Projects a <see cref="ScenePose"/> onto the realtime <see cref="SceneEventMessage"/>.
	/// ActorName prefers <see cref="ScenePose.ShowAsName"/>, falling back to AuthorName.
	/// </summary>
	/// <param name="actorObjId">
	/// The objid of <see cref="ScenePose.AuthorDbref"/>, resolved by the caller when the event is sent
	/// (the pose stores only the dbref), or null when the author is gone. Ignored without a pose.
	/// </param>
	public static SceneEventMessage BuildMessage(string sceneId, string eventType, ScenePose? pose,
		string? actorObjId)
	{
		if (pose is null)
		{
			return new SceneEventMessage(
				SceneId: sceneId,
				EventType: eventType,
				ActorName: string.Empty,
				PoseId: string.Empty,
				Content: string.Empty,
				Markup: string.Empty,
				Tags: [],
				Source: string.Empty,
				Location: string.Empty,
				Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ActorObjId: null);
		}

		var actor = string.IsNullOrEmpty(pose.ShowAsName) ? pose.AuthorName : pose.ShowAsName;

		return new SceneEventMessage(
			SceneId: sceneId,
			EventType: eventType,
			ActorName: actor,
			PoseId: pose.Id,
			Content: pose.Content,
			Markup: pose.Markup,
			Tags: pose.Tags,
			Source: pose.Source,
			Location: pose.OriginName,
			Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			ActorObjId: actorObjId,
			Type: pose.Type,
			Meta: pose.Meta);
	}

	private static async ValueTask<NatsConnection> GetConnectionAsync(string url)
	{
		await ConnectionsLock.WaitAsync();
		try
		{
			if (Connections.TryGetValue(url, out var existing))
				return existing;

			var nats = new NatsConnection(new NatsOpts { Url = url });
			await nats.ConnectAsync();
			Connections[url] = nats;
			return nats;
		}
		finally
		{
			ConnectionsLock.Release();
		}
	}
}
