using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Models;
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
///   GET /api/scenes?participant=#N[&amp;count=] — the scenes that character is a member of, newest first
///   GET /api/scenes/partners?participant=#N[&amp;count=] — who shares the most of those scenes with them
///   GET /api/scenes/{id}                  — one scene DTO (404 if missing / not visible)
///   GET /api/scenes/{id}/poses[?count=]   — ordered, non-deleted pose DTOs
///   GET /api/scenes/{id}/members          — member DTOs
///   GET /api/scenes/{id}/cast             — distinct display personas (strings)
///   GET /api/scenes/{id}/tags             — distinct pose tags (strings)
///
/// VISIBILITY: a public scene is readable by anyone; a non-public scene is readable only by
/// its owner or a member. The caller's character dbref is taken from the account session (the same
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

	/// <summary>Someone who shares scenes with a character: how many of the caller-visible ones.</summary>
	public record ScenePartnerDto(string Dbref, string Name, int Scenes);

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
	/// The caller's acting character, from the <c>character_dbref</c> claim. Null when the caller is
	/// anonymous, carries no character, or carries an unparseable one — such callers may only see
	/// public scenes.
	/// </summary>
	private DBRef? CallerRef => SceneVisibility.ActingCharacter(User);

	/// <summary>The caller's character in its canonical spelling, for the service calls that take a string.</summary>
	private string? CallerDbref => CallerRef?.ToString();

	/// <summary>
	/// True when <paramref name="scene"/> is visible to the caller: it is public, the caller owns
	/// it, or the caller is a member of it. Non-public scenes require an authenticated character.
	/// The rule itself lives in <see cref="SceneVisibility"/> because the scene hub asks the same
	/// question of the same scenes, and two copies of it would drift.
	/// </summary>
	private Task<bool> CanSeeAsync(Contracts.Scene scene) =>
		SceneVisibility.CanSeeAsync(sceneService, scene, CallerRef);

	/// <summary>
	/// GET /api/scenes?filter=active|recent|scheduled&amp;count=50
	/// Lists scenes by filter (recent-first; scheduled sorted by ScheduledFor ascending),
	/// restricted to scenes the caller may see. <c>count</c> caps the number returned.
	/// </summary>
	[HttpGet]
	public async Task<IActionResult> ListScenes([FromQuery] string filter = "recent", [FromQuery] int count = 50,
		[FromQuery] string? participant = null)
	{
		if (participant is not null)
		{
			return DBRef.TryParse(participant, out var member) && member is { } who
				? Ok((await VisibleScenesOfAsync(who, count)).Select(ToDto))
				: BadRequest(new { error = "participant must be a dbref such as #42." });
		}

		// Every returned scene is gated through CanSeeAsync so non-public scenes never leak to non-members.
		return Ok((await VisibleScenesAsync(filter, CallerDbref, count)).Select(ToDto));
	}

	/// <summary>
	/// GET /api/scenes/partners?participant=#42&amp;count=6
	/// The characters who share the most of <paramref name="participant"/>'s last 50 caller-visible
	/// scenes with them, most shared first (ties by name), without the character itself. The profile's
	/// "Often plays with" card. Counted from membership edges, so a character who only watched is counted.
	/// </summary>
	[HttpGet("partners")]
	public async Task<IActionResult> GetPartners([FromQuery] string participant, [FromQuery] int count = 6)
	{
		if (!DBRef.TryParse(participant, out var parsed) || parsed is not { } who)
		{
			return BadRequest(new { error = "participant must be a dbref such as #42." });
		}

		var shared = new Dictionary<int, (string Dbref, string Name, int Scenes)>();
		foreach (var scene in await VisibleScenesOfAsync(who, 50))
		{
			if (await sceneService.GetMembersAsync(scene.Id) is not IReadOnlyList<SceneMember> members) continue;

			foreach (var member in members.DistinctBy(m => m.MemberDbref))
			{
				if (!DBRef.TryParse(member.MemberDbref, out var memberRef) || memberRef is not { } other
					|| other.SameObjectAs(who)) continue;

				var entry = shared.TryGetValue(other.Number, out var seen)
					? seen with { Scenes = seen.Scenes + 1 }
					: ($"#{other.Number}", member.MemberName, 1);
				shared[other.Number] = entry;
			}
		}

		return Ok(shared.Values
			.OrderByDescending(p => p.Scenes)
			.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.Take(Math.Max(0, count))
			.Select(p => new ScenePartnerDto(p.Dbref, p.Name, p.Scenes)));
	}

	/// <summary>The scenes <paramref name="member"/> belongs to, newest first, that the caller may see.</summary>
	private Task<List<Contracts.Scene>> VisibleScenesOfAsync(DBRef member, int count) =>
		VisibleScenesAsync("mine", member.ToString(), count);

	/// <summary>
	/// The first <paramref name="count"/> scenes of <paramref name="filter"/> that the caller may see. The
	/// service's own <c>count</c> cuts the list before visibility is known, so the window is widened until
	/// it holds <paramref name="count"/> visible scenes or the service has no more to give — newer scenes
	/// the caller cannot open never push the older ones it can out of the answer.
	/// </summary>
	private async Task<List<Contracts.Scene>> VisibleScenesAsync(string filter, string? viewer, int count)
	{
		var wanted = Math.Max(0, count);
		var seen = new Dictionary<string, bool>(StringComparer.Ordinal);
		var ask = Math.Max(1, wanted);
		while (true)
		{
			var scenes = await sceneService.ListScenesAsync(filter, viewer, count: ask);
			var visible = new List<Contracts.Scene>(Math.Min(scenes.Count, wanted));
			foreach (var scene in scenes)
			{
				if (!seen.TryGetValue(scene.Id, out var canSee))
				{
					seen[scene.Id] = canSee = await CanSeeAsync(scene);
				}

				if (canSee && visible.Count < wanted)
				{
					visible.Add(scene);
				}
			}

			if (visible.Count >= wanted || scenes.Count < ask || ask >= int.MaxValue / 2)
			{
				return visible;
			}

			ask *= 2;
		}
	}

	/// <summary>
	/// GET /api/scenes/{id}
	/// Returns one scene, or 404 when it does not exist or the caller may not see it.
	/// </summary>
	[HttpGet("{id}")]
	public async Task<IActionResult> GetScene(string id)
	{
		if (await sceneService.GetSceneAsync(id) is not Contracts.Scene scene) return NotFound();

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
		if (await sceneService.GetSceneAsync(id) is not Contracts.Scene scene || !await CanSeeAsync(scene)) return NotFound();

		return await sceneService.GetPosesAsync(id, count: count) is IReadOnlyList<ScenePose> poses
			? Ok(poses.Select(ToDto))
			: NotFound();
	}

	/// <summary>
	/// GET /api/scenes/{id}/members
	/// Returns the scene's members, or 404 when the scene is missing or not visible.
	/// </summary>
	[HttpGet("{id}/members")]
	public async Task<IActionResult> GetMembers(string id)
	{
		if (await sceneService.GetSceneAsync(id) is not Contracts.Scene scene || !await CanSeeAsync(scene)) return NotFound();

		return await sceneService.GetMembersAsync(id) is IReadOnlyList<SceneMember> members
			? Ok(members.Select(ToDto))
			: NotFound();
	}

	/// <summary>
	/// GET /api/scenes/{id}/cast
	/// Returns the distinct display personas used in the scene, or 404 when missing / not visible.
	/// </summary>
	[HttpGet("{id}/cast")]
	public async Task<IActionResult> GetCast(string id)
	{
		if (await sceneService.GetSceneAsync(id) is not Contracts.Scene scene || !await CanSeeAsync(scene)) return NotFound();

		return await sceneService.GetCastAsync(id) is IReadOnlyList<string> cast
			? Ok(cast)
			: NotFound();
	}

	/// <summary>
	/// GET /api/scenes/{id}/tags
	/// Returns the distinct opaque tags across the scene's poses, or 404 when missing / not visible.
	/// </summary>
	[HttpGet("{id}/tags")]
	public async Task<IActionResult> GetTags(string id)
	{
		if (await sceneService.GetSceneAsync(id) is not Contracts.Scene scene || !await CanSeeAsync(scene)) return NotFound();

		return await sceneService.GetTagsAsync(id) is IReadOnlyList<string> tags
			? Ok(tags)
			: NotFound();
	}
}
