namespace SharpMUSH.Plugins.Scene.Contracts;

/// <summary>
/// A real-time scene mutation broadcast on <c>game.scene.{id}</c> (NATS) and
/// forwarded to the SignalR <c>scene:{id}</c> group. Mirrors
/// <see cref="RoomEventMessage"/>. Carries the pose payload plus opaque tags so
/// the portal can filter client-side with no round-trip; the wire is plain
/// <see cref="Content"/> + raw <see cref="Markup"/> (rendered client-side).
/// </summary>
/// <param name="SceneId">The scene the event belongs to (the SignalR group key).</param>
/// <param name="EventType">Opaque event kind: "pose" | "edit" | "delete" | "move" | "meta".</param>
/// <param name="ActorName">Display name for the actor — the pose's ShowAsName (falling back to AuthorName).</param>
/// <param name="PoseId">The affected pose id (empty for scene-level "meta" events).</param>
/// <param name="Content">Plain text (ANSI-stripped) of the affected pose's current edit.</param>
/// <param name="Markup">Raw MString markup of the affected pose's current edit.</param>
/// <param name="Tags">Opaque pose tags for client-side filtering.</param>
/// <param name="Source">Opaque pose source label.</param>
/// <param name="Location">Snapshot of the pose's origin room name.</param>
/// <param name="Timestamp">UTC Unix-millis of the event.</param>
public record SceneEventMessage(
	string SceneId,
	string EventType,
	string ActorName,
	string PoseId,
	string Content,
	string Markup,
	IReadOnlyList<string> Tags,
	string Source,
	string Location,
	long Timestamp);
