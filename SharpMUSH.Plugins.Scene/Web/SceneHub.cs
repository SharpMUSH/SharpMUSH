using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Storage;

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
///
/// <para><b>AUTHORIZATION.</b> Group membership on this hub IS the subscription to a scene's live contents,
/// so joining a group is a read of the scene and is gated exactly like one. Three layers, outermost first:
/// <list type="number">
///   <item><see cref="AuthorizeAttribute"/> on the hub — same primitive and same (default) scheme as the
///   host's <c>GameHub</c>, i.e. <c>AccountSession</c> in production and <c>DebugAuth</c> in development.
///   An anonymous connection is refused at the endpoint and never reaches a hub method.</item>
///   <item>A usable <c>character_dbref</c> claim, checked per method: <i>joining a Scene requires a
///   Character; Guests cannot join scenes.</i> An authenticated account acting as no character is refused
///   even for a public scene.</item>
///   <item>Scene visibility — public, owned by the caller, or the caller is a member — through the shared
///   <see cref="SceneVisibility"/> predicate the REST controller uses, so the live feed can never expose a
///   scene the archive would 404 on.</item>
/// </list>
/// Layer 3 is deliberately separate from layer 2: were the character requirement ever judged sufficient on
/// its own, deleting the <see cref="SceneVisibility.CanSeeAsync"/> call from <see cref="JoinScene"/> relaxes
/// it without touching anything else.</para>
/// </summary>
[Authorize]
public sealed class SceneHub(ISceneService sceneService, ILogger<SceneHub> logger) : Hub
{
	/// <summary>The SignalR client method the portal subscribes to for live scene events.</summary>
	public const string ReceiveSceneMessageMethod = "ReceiveSceneMessage";

	/// <summary>
	/// The refusal a caller sees for a scene that does not exist AND for one they may not see. One message
	/// for both on purpose: the REST side answers 404 to the same two cases, and a distinguishable refusal
	/// would let a caller enumerate private scene ids.
	/// </summary>
	private const string SceneUnavailable = "That scene is not available.";

	/// <summary>The refusal a caller with no acting character sees. Naming the reason is safe — it leaks nothing about the scene.</summary>
	private const string CharacterRequired = "Joining a scene requires a character; guests cannot join scenes.";

	/// <summary>The SignalR scene group key — mirrors the bridge leg's <c>scene:{id}</c>.</summary>
	public static string SceneGroupName(string sceneId) => $"scene:{sceneId}";

	/// <summary>
	/// Adds the calling connection to the <c>scene:{sceneId}</c> group (live scene view opened), after
	/// proving the caller is a character who may see that scene.
	/// </summary>
	/// <param name="sceneId">The scene's id. Nullable because a client can send JSON <c>null</c> regardless
	/// of the declared type — nullable reference types are not enforced at runtime.</param>
	/// <exception cref="HubException">The id is absent, the connection carries no character, or the scene
	/// does not exist / is not visible to that character.</exception>
	public async Task JoinScene(string? sceneId)
	{
		var id = RequireSceneId(sceneId);
		var character = RequireCharacter();

		var result = await sceneService.GetSceneAsync(id);
		if (result.IsT1 || !await SceneVisibility.CanSeeAsync(sceneService, result.AsT0, character))
		{
			logger.LogWarning(
				"[SceneHub] Connection {ConnectionId} (char:{Character}) was refused scene group {Group}: " +
				"the scene is missing or not visible to that character",
				Context.ConnectionId, character, SceneGroupName(id));
			throw new HubException(SceneUnavailable);
		}

		await Groups.AddToGroupAsync(Context.ConnectionId, SceneGroupName(id));
		logger.LogDebug("[SceneHub] Connection {ConnectionId} joined scene group {Group}",
			Context.ConnectionId, SceneGroupName(id));
	}

	/// <summary>
	/// Removes the calling connection from the <c>scene:{sceneId}</c> group (live scene view closed).
	/// </summary>
	/// <remarks>
	/// Gated on the character requirement (a connection that could never join has nothing to leave) but
	/// deliberately NOT on scene visibility: dropping your own subscription only ever removes access, and
	/// requiring visibility would strand a player whose membership was revoked while they were watching —
	/// they would keep receiving the scene's poses with no way to unsubscribe short of disconnecting.
	/// </remarks>
	/// <param name="sceneId">The scene's id; nullable for the same reason as in <see cref="JoinScene"/>.</param>
	/// <exception cref="HubException">The id is absent or the connection carries no character.</exception>
	public async Task LeaveScene(string? sceneId)
	{
		var id = RequireSceneId(sceneId);
		RequireCharacter();

		await Groups.RemoveFromGroupAsync(Context.ConnectionId, SceneGroupName(id));
		logger.LogDebug("[SceneHub] Connection {ConnectionId} left scene group {Group}",
			Context.ConnectionId, SceneGroupName(id));
	}

	/// <summary>
	/// The caller's acting character, or a refusal. The claim is read exactly as the REST side reads it
	/// (<see cref="SceneVisibility.ActingCharacter"/>) so the two surfaces cannot disagree about who is
	/// calling. A bare dbref is accepted here — unlike <c>GameHub</c>, which needs an objid to name a
	/// routing group — because the group key is the scene id and the reference is only ever compared
	/// against a scene's owner/member edge.
	/// </summary>
	private DBRef RequireCharacter()
	{
		if (SceneVisibility.ActingCharacter(Context.User) is { } character) return character;

		// Refusals throw rather than no-op: a silently ignored join leaves the client believing it is
		// subscribed while nothing is ever delivered, which is invisible in the browser and indistinguishable
		// from a quiet scene. The portal handles the exception (SceneLive.razor) instead.
		logger.LogWarning(
			"[SceneHub] Connection {ConnectionId} has no usable {Claim} claim (absent, unparseable, or an " +
			"account acting as no character); refusing the scene group operation",
			Context.ConnectionId, SceneVisibility.CharacterDbrefClaim);
		throw new HubException(CharacterRequired);
	}

	/// <summary>Rejects an absent scene id — client input is the one place a missing id can enter.</summary>
	private string RequireSceneId(string? sceneId)
	{
		if (!string.IsNullOrWhiteSpace(sceneId)) return sceneId;

		logger.LogWarning("[SceneHub] Connection {ConnectionId} sent an empty scene id", Context.ConnectionId);
		throw new HubException(SceneUnavailable);
	}
}
