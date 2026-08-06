using System.Security.Claims;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// The one implementation of "may this caller see this scene", shared by the plugin's two web surfaces:
/// <see cref="SceneController"/> (REST) and <see cref="SceneHub"/> (SignalR). Both answer the same question
/// about the same scenes, and a second copy of an authorization rule is how the two drift — the REST side
/// once denied owners their own private scenes while the hub let anyone into any scene's broadcast group,
/// which is precisely the shape of bug a single predicate prevents.
///
/// <para>The hub layers an ADDITIONAL requirement on top of this (a scene subscription requires a character
/// at all, so guests never join even a public scene); that rule lives at the hub because it is a hub rule.
/// This type is only the visibility half.</para>
/// </summary>
internal static class SceneVisibility
{
	/// <summary>
	/// Claim name that carries the authenticated character's dbref — mirrors the host's
	/// <c>GameHub.CharacterDbrefClaim</c>. Inlined here so the plugin takes no dependency on the
	/// Server's hub types.
	/// </summary>
	internal const string CharacterDbrefClaim = "character_dbref";

	/// <summary>
	/// The caller's acting character, parsed from the <c>character_dbref</c> claim. Null when the caller
	/// is anonymous, carries no character, or carries an unparseable one.
	/// </summary>
	/// <remarks>
	/// Parsed rather than kept as the raw claim string because the two references being compared are
	/// spelled differently: the claim is a full objid (<c>#1:1785989066109</c>, minted from the session's
	/// character) while a scene's owner/member dbref is resolved from a live object edge and comes back
	/// bare (<c>#1</c>). String comparison between those two forms can never succeed, which is what used
	/// to make every non-public scene invisible to its own owner.
	/// </remarks>
	internal static DBRef? ActingCharacter(ClaimsPrincipal? user) =>
		DBRef.TryParse(user?.FindFirst(CharacterDbrefClaim)?.Value, out var parsed) ? parsed : null;

	/// <summary>
	/// True when <paramref name="scene"/> is visible to <paramref name="caller"/>: it is public, the caller
	/// owns it, or the caller is a member of it. Non-public scenes require a character
	/// (<paramref name="caller"/> is null for anonymous/characterless callers).
	/// </summary>
	internal static async Task<bool> CanSeeAsync(ISceneService sceneService, Contracts.Scene scene, DBRef? caller)
	{
		if (scene.IsPublic) return true;

		if (caller is not { } me) return false;

		// Owner always sees their own scene. Both sides here are resolved live in this request — the owner
		// by dereferencing the scene's owner edge to its object vertex, the caller from the session's
		// current character — so two live objects cannot share the number and DBRef.SameObjectAs is exact;
		// it still compares the creation stamp on the side that has one.
		if (scene.OwnerDbref is { } owner && DBRef.TryParse(owner, out var ownerRef) && ownerRef is { } o
				&& o.SameObjectAs(me))
			return true;

		// Otherwise the caller must hold a membership edge on the scene. The storage resolves the reference
		// against the live object itself (rejecting a stale objid), so hand it the same canonical spelling.
		var member = await sceneService.GetMemberAsync(scene.Id, me.ToString());
		return member.IsT0;
	}
}
