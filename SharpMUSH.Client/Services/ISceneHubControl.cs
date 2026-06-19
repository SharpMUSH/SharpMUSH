namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-only control surface for the scene-group leg of the GameHub connection.
/// The shared <c>IConnectionStateService</c> lives in SharpMUSH.Library and cannot be
/// extended here, so scene group membership is exposed through this small interface,
/// implemented by the same <see cref="ConnectionStateService"/> singleton.
/// </summary>
public interface ISceneHubControl
{
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
