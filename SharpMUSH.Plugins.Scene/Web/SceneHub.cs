using Microsoft.AspNetCore.SignalR;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// The Scene plugin's own SignalR hub, mapped at <c>/hubs/scene</c> by the plugin's
/// <c>ScenePlugin.MapEndpoints</c> (<see cref="SharpMUSH.Library.Plugins.IEndpointContributor"/>). Phase 9
/// moved the scene realtime leg out of the Server's <c>GameHub</c> so the host carries no scene-specific hub
/// surface — removing the plugin leaves the host with no scene realtime endpoint at all.
///
/// <para><b>Deliberately a plain (non-generic) <see cref="Hub"/>, not <c>Hub&lt;ISceneHubClient&gt;</c>.</b>
/// SignalR's strongly-typed client surface uses <c>Reflection.Emit</c> (<c>TypedClientBuilder&lt;T&gt;</c>) to
/// generate a proxy for the client interface. Because this hub loads in the plugin's <i>collectible</i>
/// AssemblyLoadContext, a strongly-typed client interface would make SignalR emit a proxy in a
/// non-collectible dynamic assembly that references a collectible type — which throws
/// <c>NotSupportedException: A non-collectible assembly may not reference a collectible assembly</c> at host
/// startup. A plain hub + non-generic <c>IHubContext</c>.<c>SendAsync("ReceiveSceneMessage", …)</c> avoids
/// the emit entirely while keeping the same wire contract the portal subscribes to.</para>
///
/// <para>Client-to-server: <see cref="JoinScene"/> / <see cref="LeaveScene"/> manage the <c>scene:{id}</c>
/// group membership. Server-to-client pushes come from the plugin's <c>IBridgeSubscriptionSource</c> leg,
/// which forwards <c>game.scene.*</c> NATS messages to the matching group via the non-generic
/// <c>IHubContext&lt;SceneHub&gt;</c>.</para>
/// </summary>
public sealed class SceneHub : Hub
{
	/// <summary>The SignalR client method the portal subscribes to for live scene events.</summary>
	public const string ReceiveSceneMessageMethod = "ReceiveSceneMessage";

	/// <summary>The SignalR scene group key — mirrors the bridge leg's <c>scene:{id}</c>.</summary>
	public static string SceneGroupName(string sceneId) => $"scene:{sceneId}";

	/// <summary>Adds the calling connection to the <c>scene:{sceneId}</c> group (live scene view opened).</summary>
	public Task JoinScene(string sceneId) =>
		Groups.AddToGroupAsync(Context.ConnectionId, SceneGroupName(sceneId));

	/// <summary>Removes the calling connection from the <c>scene:{sceneId}</c> group (live scene view closed).</summary>
	public Task LeaveScene(string sceneId) =>
		Groups.RemoveFromGroupAsync(Context.ConnectionId, SceneGroupName(sceneId));
}
