using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// Read-only REST API for the graph-native Scene System, consumed by the Blazor WASM
/// portal (which has no local scene service of its own). Writes happen exclusively through
/// the game (the wizard-only <c>@scene</c> command / softcode) — these endpoints only read.
///
/// <para>Phase 9: this controller moved OUT of <c>SharpMUSH.Server</c> into the Scene plugin. It is
/// discovered through the MVC ApplicationPart the plugin registers in its
/// <c>IServiceRegistrar.RegisterServices</c> (<c>AddControllers().AddApplicationPart(thisAssembly)</c>),
/// so the route <c>api/scenes</c> is served identically — but the host carries no scene controller, and
/// removing the plugin removes the scene REST surface entirely.</para>
///
/// Routes:
///   GET /api/scenes?filter=active|recent|scheduled[&amp;count=] — list scene DTOs
///   GET /api/scenes/{id}                  — one scene DTO (404 if missing / not visible)
///   GET /api/scenes/{id}/poses[?count=]   — ordered, non-deleted pose DTOs
///   GET /api/scenes/{id}/members          — member DTOs
///   GET /api/scenes/{id}/cast             — distinct display personas (strings)
///   GET /api/scenes/{id}/tags             — distinct pose tags (strings)
///
/// VISIBILITY: a public scene is readable by anyone; a non-public scene is readable only by
/// its owner or a member. The caller's character dbref is taken from the JWT (the same
/// <c>character_dbref</c> claim the host's SignalR routing uses); an anonymous caller sees only
/// public scenes.
/// <see cref="ISceneService"/> is registered by the plugin's <c>IServiceRegistrar</c> over the active
/// provider's host-shared storage accessor, so it is injected directly here.
/// </summary>
[ApiController]
[Route("api/scenes")]
[AllowAnonymous]
public class SceneController(ISceneService sceneService) : ControllerBase
{
	/// <summary>
	/// Claim name that carries the authenticated character's dbref — mirrors the host's
	/// <c>GameHub.CharacterDbrefClaim</c>. Inlined here so the plugin controller takes no dependency
	/// on the Server's hub types.
	/// </summary>
	private const string CharacterDbrefClaim = "character_dbref";

	/// <summary>Scene data returned by the API. Timestamps are UTC Unix-millis (long).</summary>
	public record SceneDto(
		string Id,
		string Status,
		bool IsPublic,
		bool IsTempRoom,
		long? ScheduledFor,
		long StartedAt,
		long LastActivityAt,
		int PoseCount,
		string? OwnerDbref,
		string OwnerName,
		string? StarterDbref,
		string StarterName,
		string? RoomDbref,
		string RoomName,
		IReadOnlyDictionary<string, string> Meta);

	/// <summary>One pose (the projected current edit). Timestamps are UTC Unix-millis (long).</summary>
	public record ScenePoseDto(
		string Id,
		string SceneId,
		string? AuthorDbref,
		string AuthorName,
		string ShowAsName,
		string? OriginDbref,
		string OriginName,
		string Source,
		IReadOnlyList<string> Tags,
		IReadOnlyDictionary<string, string> Meta,
		long CreatedAt,
		bool IsDeleted,
		string Content,
		string Markup,
		int EditCount,
		long? LastEditedAt,
		string? LastEditorDbref,
		string? LastEditorName);

	/// <summary>A player's participation edge. Timestamp is UTC Unix-millis (long).</summary>
	public record SceneMemberDto(
		string SceneId,
		string? MemberDbref,
		string MemberName,
		string Role,
		string ShowAs,
		bool IsCurrent,
		long GrantedAt);

	private static SceneDto ToDto(Contracts.Scene s) => new(
		s.Id, s.Status, s.IsPublic, s.IsTempRoom, s.ScheduledFor, s.StartedAt, s.LastActivityAt,
		s.PoseCount, s.OwnerDbref, s.OwnerName, s.StarterDbref, s.StarterName, s.RoomDbref, s.RoomName, s.Meta);

	private static ScenePoseDto ToDto(ScenePose p) => new(
		p.Id, p.SceneId, p.AuthorDbref, p.AuthorName, p.ShowAsName, p.OriginDbref, p.OriginName,
		p.Source, p.Tags, p.Meta, p.CreatedAt, p.IsDeleted, p.Content, p.Markup, p.EditCount,
		p.LastEditedAt, p.LastEditorDbref, p.LastEditorName);

	private static SceneMemberDto ToDto(SceneMember m) => new(
		m.SceneId, m.MemberDbref, m.MemberName, m.Role, m.ShowAs, m.IsCurrent, m.GrantedAt);

	/// <summary>
	/// The caller's character dbref, taken from the <c>character_dbref</c> JWT claim. Null when the
	/// caller is anonymous or carries no character — such callers may only see public scenes.
	/// </summary>
	private string? CallerDbref => User.FindFirst(CharacterDbrefClaim)?.Value;

	/// <summary>
	/// Normalises a dbref for comparison. The owner edge resolves to a bare object key
	/// (e.g. <c>1</c>) while the JWT <c>character_dbref</c> claim is hash-prefixed
	/// (e.g. <c>#1</c>); stripping a leading <c>#</c> makes the two comparable.
	/// </summary>
	private static string NormalizeDbref(string dbref) =>
		dbref.StartsWith('#') ? dbref[1..] : dbref;

	/// <summary>
	/// True when <paramref name="scene"/> is visible to the caller: it is public, the caller owns
	/// it, or the caller is a member of it. Non-public scenes require an authenticated character.
	/// </summary>
	private async Task<bool> CanSeeAsync(Contracts.Scene scene)
	{
		if (scene.IsPublic) return true;

		var me = CallerDbref;
		if (string.IsNullOrEmpty(me)) return false;

		// Owner always sees their own scene (resolved from the live owner edge).
		if (scene.OwnerDbref is { } owner &&
		    string.Equals(NormalizeDbref(owner), NormalizeDbref(me), StringComparison.Ordinal))
			return true;

		// Otherwise the caller must hold a membership edge on the scene. The service accepts
		// the hash-prefixed dbref the claim carries.
		var member = await sceneService.GetMemberAsync(scene.Id, me);
		return member.IsT0;
	}

	/// <summary>
	/// GET /api/scenes?filter=active|recent|scheduled&amp;count=50
	/// Lists scenes by filter (recent-first; scheduled sorted by ScheduledFor ascending),
	/// restricted to scenes the caller may see. <c>count</c> caps the number returned.
	/// </summary>
	[HttpGet]
	public async Task<IActionResult> ListScenes([FromQuery] string filter = "recent", [FromQuery] int count = 50)
	{
		// The service applies its own viewer-scoped visibility filtering; we additionally gate each
		// returned scene through CanSeeAsync so non-public scenes never leak to non-members.
		var scenes = await sceneService.ListScenesAsync(filter, CallerDbref, count: count);

		var visible = new List<SceneDto>(scenes.Count);
		foreach (var scene in scenes)
			if (await CanSeeAsync(scene))
				visible.Add(ToDto(scene));

		return Ok(visible);
	}

	/// <summary>
	/// GET /api/scenes/{id}
	/// Returns one scene, or 404 when it does not exist or the caller may not see it.
	/// </summary>
	[HttpGet("{id}")]
	public async Task<IActionResult> GetScene(string id)
	{
		var result = await sceneService.GetSceneAsync(id);
		if (result.IsT1) return NotFound();

		var scene = result.AsT0;
		return await CanSeeAsync(scene) ? Ok(ToDto(scene)) : NotFound();
	}

	/// <summary>
	/// GET /api/scenes/{id}/poses?count=
	/// Returns the scene's poses in chain order (optionally only the last <c>count</c>),
	/// or 404 when the scene is missing or not visible.
	/// </summary>
	[HttpGet("{id}/poses")]
	public async Task<IActionResult> GetPoses(string id, [FromQuery] int? count = null)
	{
		var sceneResult = await sceneService.GetSceneAsync(id);
		if (sceneResult.IsT1 || !await CanSeeAsync(sceneResult.AsT0)) return NotFound();

		var poses = await sceneService.GetPosesAsync(id, count: count);
		return poses.Match<IActionResult>(
			list => Ok(list.Select(ToDto)),
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/scenes/{id}/members
	/// Returns the scene's members, or 404 when the scene is missing or not visible.
	/// </summary>
	[HttpGet("{id}/members")]
	public async Task<IActionResult> GetMembers(string id)
	{
		var sceneResult = await sceneService.GetSceneAsync(id);
		if (sceneResult.IsT1 || !await CanSeeAsync(sceneResult.AsT0)) return NotFound();

		var members = await sceneService.GetMembersAsync(id);
		return members.Match<IActionResult>(
			list => Ok(list.Select(ToDto)),
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/scenes/{id}/cast
	/// Returns the distinct display personas used in the scene, or 404 when missing / not visible.
	/// </summary>
	[HttpGet("{id}/cast")]
	public async Task<IActionResult> GetCast(string id)
	{
		var sceneResult = await sceneService.GetSceneAsync(id);
		if (sceneResult.IsT1 || !await CanSeeAsync(sceneResult.AsT0)) return NotFound();

		var cast = await sceneService.GetCastAsync(id);
		return cast.Match<IActionResult>(
			list => Ok(list),
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/scenes/{id}/tags
	/// Returns the distinct opaque tags across the scene's poses, or 404 when missing / not visible.
	/// </summary>
	[HttpGet("{id}/tags")]
	public async Task<IActionResult> GetTags(string id)
	{
		var sceneResult = await sceneService.GetSceneAsync(id);
		if (sceneResult.IsT1 || !await CanSeeAsync(sceneResult.AsT0)) return NotFound();

		var tags = await sceneService.GetTagsAsync(id);
		return tags.Match<IActionResult>(
			list => Ok(list),
			_ => NotFound());
	}
}
