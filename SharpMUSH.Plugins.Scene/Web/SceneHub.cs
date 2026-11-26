using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Models;

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
///   An anonymous connection is refused at the endpoint and never reaches a hub method. The policy
///   (<see cref="DelegatedScopes.ScenesRead"/>) also takes an app's access token, as the character the player allowed
///   it to act for, when the player allowed it <c>scenes.read</c>; a token without that scope is refused here too. The
///   host ends the app's connection when its token expires (the plugin maps the hub with
///   <c>CloseOnAuthenticationExpiration</c>), so a revoked app stops hearing scenes within the token's hour.</item>
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
[Authorize(Policy = DelegatedScopes.PolicyPrefix + DelegatedScopes.ScenesRead)]
public sealed class SceneHub(ISceneService sceneService, ISceneReadAccess readAccess, ILogger<SceneHub> logger,
	SceneAppConnections? apps = null) : Hub
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
	/// One character's connections on a scene: where a notice for that member alone goes (<c>@scene/notice</c>).
	/// A connection joins it with the scene's group, so only a character that may see the scene is in it.
	/// </summary>
	public static string MemberGroupName(string sceneId, int character) => $"scene:{sceneId}:char:{character}";

	/// <summary>
	/// The connections on a scene of characters who see every scene (wizards). An event for a hidden scene goes
	/// here too, since such a character is not on the scene's list.
	/// </summary>
	public static string SeesEveryGroupName(string sceneId) => $"scene:{sceneId}:every";

	/// <summary>
	/// The group an event goes to: a notice for one member (<see cref="SceneEventMessage.Recipient"/>) reaches only
	/// that member's connections on the scene, everything else the whole scene.
	/// </summary>
	public static string GroupFor(SceneEventMessage message) =>
		message.Recipient is { } recipient && DBRef.TryParse(recipient, out var member) && member is { } to
			? MemberGroupName(message.SceneId, to.Number)
			: SceneGroupName(message.SceneId);

	/// <summary>
	/// The groups an event is sent to. Visibility is checked only when a connection joins the scene's group, and a
	/// private scene that ends becomes hidden while those who joined to watch it are still in it; so an event for a
	/// hidden scene skips the scene's group and goes to its owner's, members' and invited players' connections on it instead,
	/// and to those of characters who see every scene.
	/// </summary>
	public static async Task<IReadOnlyList<string>> GroupsForAsync(ISceneService scenes, SceneEventMessage message)
	{
		if (message.Recipient is not null
			|| await scenes.GetSceneAsync(message.SceneId) is not Contracts.Scene { IsHidden: true } scene)
			return [GroupFor(message)];

		// Invitations first: a join turns an invitation into a membership in one write, so a player who accepts
		// between the two reads is in the members read second rather than in neither.
		var invited = await scenes.GetInvitationsAsync(scene.Id) is IReadOnlyList<SceneInvitation> pending ? pending : [];
		var members = await scenes.GetMembersAsync(scene.Id) is IReadOnlyList<SceneMember> found ? found : [];
		return [.. members.Select(m => m.MemberDbref).Concat(invited.Select(i => i.PlayerDbref)).Append(scene.OwnerDbref)
			.Select(dbref => DBRef.TryParse(dbref, out var parsed) ? parsed : null)
			.OfType<DBRef>()
			.Select(dbref => MemberGroupName(scene.Id, dbref.Number))
			.Append(SeesEveryGroupName(scene.Id))
			.Distinct()];
	}

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
		await RequireLiveAppTokenAsync(character);

		// An app sees hidden scenes as a wizard only when it was allowed staff powers (game) too.
		var seesEvery = SceneAppAccess.MaySeeEvery(Context.User) && await readAccess.SeesEveryAsync(character);
		if (await sceneService.GetSceneAsync(id) is not Contracts.Scene scene
			|| !await SceneVisibility.CanSeeAsync(sceneService, scene, character, await readAccess.MayReadAsync(character),
				seesEvery))
		{
			logger.LogWarning(
				"[SceneHub] Connection {ConnectionId} (char:{Character}) was refused scene group {Group}: " +
				"the scene is missing or not visible to that character",
				Context.ConnectionId, character, SceneGroupName(id));
			throw new HubException(SceneUnavailable);
		}

		await Groups.AddToGroupAsync(Context.ConnectionId, SceneGroupName(id));
		await Groups.AddToGroupAsync(Context.ConnectionId, MemberGroupName(id, character.Number));
		if (seesEvery)
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, SeesEveryGroupName(id));
			apps?.SeesEvery(Context.ConnectionId);
		}
		logger.LogDebug("[SceneHub] Connection {ConnectionId} joined scene group {Group}",
			Context.ConnectionId, SceneGroupName(id));
	}

	/// <summary>An app's connection is watched, so it closes once its token stops working.</summary>
	public override Task OnConnectedAsync()
	{
		if (DelegatedIdentity.App(Context.User) is not null && apps is not null
				&& SceneAppConnections.TokenOf(Context.GetHttpContext()) is { } token
				&& SceneVisibility.ActingCharacter(Context.User) is { } character)
			apps.Opened(Context.ConnectionId, token, character, Context);
		return base.OnConnectedAsync();
	}

	public override Task OnDisconnectedAsync(Exception? exception)
	{
		apps?.Closed(Context.ConnectionId);
		return base.OnDisconnectedAsync(exception);
	}

	/// <summary>
	/// For an app, the token it connected with must still work: a connection keeps the caller it opened with, and a
	/// revoked app must not join another scene in the time before its connection is closed. A player's own session is
	/// not asked.
	/// </summary>
	private async Task RequireLiveAppTokenAsync(DBRef character)
	{
		if (DelegatedIdentity.App(Context.User) is null) return;
		if (apps is not null && SceneAppConnections.TokenOf(Context.GetHttpContext()) is { } token
				&& await apps.StillAllowedAsync(token, character))
			return;

		logger.LogWarning("[SceneHub] Connection {ConnectionId} (char:{Character}) is an app whose token no longer works",
			Context.ConnectionId, character);
		throw new HubException(SceneUnavailable);
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
		var character = RequireCharacter();

		await Groups.RemoveFromGroupAsync(Context.ConnectionId, SceneGroupName(id));
		await Groups.RemoveFromGroupAsync(Context.ConnectionId, MemberGroupName(id, character.Number));
		await Groups.RemoveFromGroupAsync(Context.ConnectionId, SeesEveryGroupName(id));
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
