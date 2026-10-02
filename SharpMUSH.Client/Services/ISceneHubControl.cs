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
	/// scene's realtime <c>ReceiveSceneMessage</c> events. No-ops when not connected, and when the
	/// connection drops mid-call — see <see cref="LeaveSceneAsync"/>.
	/// </summary>
	/// <exception cref="Microsoft.AspNetCore.SignalR.HubException">The hub refused the join: this
	/// connection acts as no character, or the scene does not exist / is not visible to it.</exception>
	Task JoinSceneAsync(string sceneId);

	/// <summary>
	/// Leaves the SignalR <c>scene:{sceneId}</c> group. No-ops when not connected — including when the
	/// connection closes between the state check and the invoke, which callers cannot pre-empt and have
	/// nothing to do about: the group membership dies with the connection either way.
	/// </summary>
	/// <exception cref="Microsoft.AspNetCore.SignalR.HubException">The hub refused the leave: this
	/// connection acts as no character.</exception>
	Task LeaveSceneAsync(string sceneId);

	/// <summary>
	/// Whether scene events can arrive: the scene connection is up. Joining while it is down records the
	/// scene, so the join happens once it comes back, but nothing arrives in between.
	/// </summary>
	bool IsSceneLive { get; }

	/// <summary>Raised when <see cref="IsSceneLive"/> may have changed, and after a reconnect rejoins the scenes.</summary>
	event Action? OnSceneLiveChanged;

	/// <summary>
	/// Brings the scene connection up if it is down: the game connection first when that is down too, then a
	/// fresh scene connection, rejoining every scene joined so far. A no-op while it is up.
	/// </summary>
	Task EnsureSceneLiveAsync();
}
