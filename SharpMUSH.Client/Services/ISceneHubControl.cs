using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-only control surface for the plugin-owned scene realtime hub (<c>/hubs/scene</c>).
/// The shared <c>IConnectionStateService</c> in SharpMUSH.Library is deliberately scene-agnostic
/// (the engine cannot name a runtime-loaded plugin's types), so scene group membership AND the
/// scene-event stream are both exposed here, on the same <see cref="ConnectionStateService"/> singleton,
/// using the client's own <see cref="SceneEventMessage"/> DTO (deserialized from the hub's JSON wire).
/// </summary>
public interface ISceneHubControl
{
	/// <summary>Raised when the scene hub pushes a <see cref="SceneEventMessage"/> to this client.</summary>
	event Action<SceneEventMessage>? OnSceneEventReceived;

	/// <summary>
	/// Joins the SignalR <c>scene:{sceneId}</c> group so this client receives the
	/// scene's realtime <c>ReceiveSceneMessage</c> events. No-ops when not connected.
	/// </summary>
	Task JoinSceneAsync(string sceneId);

	/// <summary>
	/// Leaves the SignalR <c>scene:{sceneId}</c> group. No-ops when not connected.
	/// </summary>
	Task LeaveSceneAsync(string sceneId);
}
