using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// What the portal keeps for a character about scenes, beside the read-only <see cref="SceneController"/>: the
/// unsent pose that follows a character between devices, and the scene schedule as a calendar feed.
///
/// Routes:
///   GET    /api/scenes/{id}/draft         — the acting character's draft for the scene (empty, at 0, when it has none)
///   PUT    /api/scenes/{id}/draft         — replaces it (an empty one too, so a sent pose empties every device)
///   DELETE /api/scenes/{id}/draft         — forgets it
///   GET    /api/scenes/calendar.ics       — the public scenes on the schedule, for anyone
///   GET    /api/scenes/calendar/{secret}.ics[?scope=mine] — the scenes the link's character can see, or only
///                                           the ones it runs or has joined
///   GET    /api/scenes/calendar/link      — whether the acting character has a private link, and since when
///   POST   /api/scenes/calendar/link      — makes it a new one (the old one stops working) and returns its address
///   DELETE /api/scenes/calendar/link      — turns it off
///
/// A draft is the character's own and never game state, so it is written here rather than through a scene
/// command. It is read and written only for a scene the character may see, the same rule the archive keeps.
/// </summary>
[ApiController]
[Route("api/scenes")]
[AllowAnonymous]
public class ScenePortalController(
	ISceneService sceneService,
	IScenePortalStore store,
	IOptionsWrapper<SharpMUSHOptions> options,
	TimeProvider time,
	IMediator mediator,
	ISceneReadAccess readAccess) : ControllerBase
{
	/// <summary>A draft as the portal reads it. <c>UpdatedAt</c> is UTC Unix-millis.</summary>
	public record SceneDraftDto(string Mode, string Text, string? Markup, long UpdatedAt);

	/// <summary>A draft as the portal writes it.</summary>
	public record SceneDraftRequest(string? Mode, string? Text, string? Markup);

	/// <summary>Whether the acting character has a private calendar link; <c>Url</c> only in the answer that made it.</summary>
	public record SceneCalendarLinkDto(bool Exists, long? CreatedAt, string? Url);

	/// <summary>How far back a feed keeps a scene's time, so an event stays in the calendar after it ran.</summary>
	public static readonly TimeSpan CalendarHistory = TimeSpan.FromDays(14);

	/// <summary>The most scenes one feed lists.</summary>
	public const int CalendarLimit = 500;

	private DBRef? Caller => SceneVisibility.ActingCharacter(User);

	[HttpGet("{id}/draft")]
	public async Task<IActionResult> GetDraft(string id)
	{
		if (Caller is not { } me) return Unauthorized();
		if (!await CanSeeAsync(id, me)) return NotFound();

		// No draft reads as an empty one, written at no time, so any copy a device holds is newer.
		return Ok(store.GetDraft(me.ToString(), id) is SceneDraft draft ? ToDto(draft) : new SceneDraftDto("emit", "", null, 0));
	}

	[HttpPut("{id}/draft")]
	public async Task<IActionResult> SaveDraft(string id, [FromBody] SceneDraftRequest request)
	{
		if (Caller is not { } me) return Unauthorized();
		if (!await CanSeeAsync(id, me)) return NotFound();

		var mode = (request.Mode ?? "").Trim().ToLowerInvariant();
		if (mode.Length is 0 or > 16 || !mode.All(char.IsAsciiLetterLower))
			return BadRequest(new { error = "mode must be one word, such as emit or ooc." });

		var text = request.Text ?? "";
		var markup = string.IsNullOrEmpty(request.Markup) ? null : request.Markup;
		if (text.Length > IScenePortalStore.MaxDraftLength || markup?.Length > 4 * IScenePortalStore.MaxDraftLength)
			return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "That draft is too long to keep." });

		// An empty draft is kept too, with its time: a pose sent from one device has to empty the copy another
		// device still holds, and only a newer empty draft can tell it to.
		var draft = new SceneDraft(mode, text, markup, time.GetUtcNow().ToUnixTimeMilliseconds());
		await store.SaveDraftAsync(me.ToString(), id, draft);
		return Ok(ToDto(draft));
	}

	[HttpDelete("{id}/draft")]
	public async Task<IActionResult> DeleteDraft(string id)
	{
		if (Caller is not { } me) return Unauthorized();

		// Forgetting your own draft needs no sight of the scene: a member who was removed can still clear it.
		await store.DeleteDraftAsync(me.ToString(), id);
		return NoContent();
	}

	[HttpGet("calendar.ics")]
	public async Task<IActionResult> PublicCalendar()
	{
		var mayRead = await readAccess.MayReadAsync(null, HttpContext.RequestAborted);
		return Calendar(await CalendarScenesAsync(scene => Task.FromResult(mayRead && scene.IsPublic)), options.CurrentValue.Net.MudName);
	}

	[HttpGet("calendar/{secret}.ics")]
	public async Task<IActionResult> PersonalCalendar(string secret, [FromQuery] string? scope = null)
	{
		var mine = string.Equals(scope, "mine", StringComparison.OrdinalIgnoreCase);
		if (store.FindCalendarOwner(secret) is not string owner || !DBRef.TryParse(owner, out var parsed) || parsed is not { } character)
			return NotFound();
		// The link names the character by objid. A character destroyed since, whose number now belongs to someone
		// else, must not lend the newcomer's view to the old link: the objid has to name the live object.
		if (character.CreationMilliseconds is null || await mediator.Send(new GetObjectNodeQuery(character)) is not AnySharpObject)
			return NotFound();

		var mayRead = await readAccess.MayReadAsync(character, HttpContext.RequestAborted);
		var scenes = await CalendarScenesAsync(scene => mine
			? RunsOrJoinedAsync(scene, character)
			: SceneVisibility.CanSeeAsync(sceneService, scene, character, mayRead));
		return Calendar(scenes, options.CurrentValue.Net.MudName);
	}

	[HttpGet("calendar/link")]
	public IActionResult GetCalendarLink()
	{
		if (Caller is not { } me) return Unauthorized();

		return Ok(store.GetCalendarLink(me.ToString()) is SceneCalendarLink link
			? new SceneCalendarLinkDto(true, link.CreatedAt, null)
			: new SceneCalendarLinkDto(false, null, null));
	}

	[HttpPost("calendar/link")]
	public async Task<IActionResult> CreateCalendarLink()
	{
		if (Caller is not { } me) return Unauthorized();

		var now = time.GetUtcNow().ToUnixTimeMilliseconds();
		var secret = await store.CreateCalendarLinkAsync(me.ToString(), now);
		return Ok(new SceneCalendarLinkDto(true, now, new Uri(Origin, $"api/scenes/calendar/{secret}.ics").AbsoluteUri));
	}

	[HttpDelete("calendar/link")]
	public async Task<IActionResult> DeleteCalendarLink()
	{
		if (Caller is not { } me) return Unauthorized();

		await store.DeleteCalendarLinkAsync(me.ToString());
		return NoContent();
	}

	private static SceneDraftDto ToDto(SceneDraft draft) => new(draft.Mode, draft.Text, draft.Markup, draft.UpdatedAt);

	private async Task<bool> CanSeeAsync(string id, DBRef caller) =>
		await sceneService.GetSceneAsync(id) is Contracts.Scene scene
		&& await SceneVisibility.CanSeeAsync(sceneService, scene, caller, await readAccess.MayReadAsync(caller, HttpContext.RequestAborted));

	/// <summary>The character owns the scene or holds a membership on it (joined, or answered its RSVP).</summary>
	private async Task<bool> RunsOrJoinedAsync(Contracts.Scene scene, DBRef character) =>
		(scene.OwnerDbref is { } owner && DBRef.TryParse(owner, out var parsed) && parsed is { } o && o.SameObjectAs(character))
		|| await sceneService.GetMemberAsync(scene.Id, character.ToString()) is SceneMember;

	/// <summary>
	/// Every scene with a time that is still to come, running, or ran within <see cref="CalendarHistory"/>, that
	/// <paramref name="include"/> keeps, in time order. A scene that finished stays, so the event does not vanish
	/// from a calendar the moment the scene ends.
	/// </summary>
	private async Task<IReadOnlyList<Contracts.Scene>> CalendarScenesAsync(Func<Contracts.Scene, Task<bool>> include)
	{
		var since = (time.GetUtcNow() - CalendarHistory).ToUnixTimeMilliseconds();
		// Windowed in the store, so scenes with no time never take a place in a list's limit.
		var candidates = (await sceneService.ListScenesAsync("scheduled", fromUtcMillis: since, count: CalendarLimit))
			.Concat(await sceneService.ListScenesAsync("active", fromUtcMillis: since, count: CalendarLimit))
			.Concat(await sceneService.ListScenesAsync("finished", fromUtcMillis: since, count: CalendarLimit))
			.Where(scene => scene.ScheduledFor >= since)
			.DistinctBy(scene => scene.Id)
			.OrderBy(scene => scene.ScheduledFor);

		var kept = new List<Contracts.Scene>();
		foreach (var scene in candidates)
		{
			if (kept.Count == CalendarLimit) break;
			if (await include(scene)) kept.Add(scene);
		}
		return kept;
	}

	private ContentResult Calendar(IEnumerable<Contracts.Scene> scenes, string gameName) => new()
	{
		Content = SceneCalendar.Write(scenes, $"{gameName} scenes", Origin, Request.Host.Host, time.GetUtcNow()),
		ContentType = SceneCalendar.ContentType,
		StatusCode = StatusCodes.Status200OK
	};

	/// <summary>The address the portal is reached at, as this request reached it (forwarded headers already applied).</summary>
	private Uri Origin => new($"{Request.Scheme}://{Request.Host}{Request.PathBase}/");
}
