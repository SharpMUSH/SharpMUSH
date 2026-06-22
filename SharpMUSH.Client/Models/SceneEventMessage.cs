namespace SharpMUSH.Client.Models;

/// <summary>
/// The client-side DTO the portal deserializes from the Scene plugin's <c>/hubs/scene</c> SignalR
/// <c>ReceiveSceneMessage</c> push. It mirrors the plugin's own <c>SceneEventMessage</c> wire shape
/// EXACTLY (by JSON property name + order), but is an independent type: the Scene plugin loads in a
/// collectible AssemblyLoadContext, so the client cannot — and must not — compile-reference the plugin's
/// type. The boundary is serialization (JSON), not a shared assembly.
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
public sealed record SceneEventMessage(
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
