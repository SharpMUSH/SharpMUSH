using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Casefiles, the portal's face of <c>@casefile</c> (<see cref="ICasefileService"/>). Every route answers as the
/// session's acting character, and a casefile the caller is not in answers 404.
///
/// Routes:
///   GET    /api/casefiles                       — the casefiles the caller is in
///   POST   /api/casefiles                       — open one (wiki.create)
///   GET    /api/casefiles/{key}                 — one casefile
///   PUT    /api/casefiles/{key}/members         — let someone in, change their level, or take them out (a lead)
///   POST   /api/casefiles/{key}/developments    — post a development (a contributor or lead)
/// </summary>
[ApiController]
[Authorize]
[Route("api/casefiles")]
public class CasefilesController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	IWikiNameResolver names,
	ICasefileService casefiles,
	IMediator mediator,
	ILogger<CasefilesController> logger) : WikiControllerBase(wikiService, localization, access, names, logger)
{
	[HttpGet]
	public async Task<IActionResult> List()
		=> Ok((await casefiles.ListAsync(await ReaderAsync())).Select(ToDto).ToList());

	[HttpGet("{key}")]
	public async Task<IActionResult> Get(string key)
		=> await casefiles.GetAsync(await ReaderAsync(), key) is Casefile casefile
			? Ok(await ToDtoAsync(casefile))
			: NotFound();

	[HttpPost]
	public async Task<IActionResult> Create([FromBody] CreateCasefileRequest request)
	{
		if (string.IsNullOrEmpty(CallerDbref)) return Unauthorized("Missing character identity.");
		return await casefiles.CreateAsync(await ReaderAsync(), request.Title ?? string.Empty, request.Briefing ?? string.Empty) switch
		{
			Casefile casefile => await CreatedAsync(casefile),
			Error<string> error => Refused(error.Value),
		};

		async Task<IActionResult> CreatedAsync(Casefile casefile)
		{
			Logger.LogInformation("Casefile opened: key={Key} by={Author}", casefile.Summary.Key, CallerDbref);
			return CreatedAtAction(nameof(Get), new { key = casefile.Summary.Key }, await ToDtoAsync(casefile));
		}
	}

	[HttpPut("{key}/members")]
	public async Task<IActionResult> SetMember(string key, [FromBody] SetCasefileMemberRequest request)
	{
		WikiMemberLevel? level = null;
		if (request.Level is { Length: > 0 } text)
		{
			if (!Enum.TryParse<WikiMemberLevel>(text, ignoreCase: true, out var parsed) || int.TryParse(text, out _))
				return BadRequest(new { error = "A level is reader, contributor or lead." });
			level = parsed;
		}

		if (await PrincipalAsync(request.Who ?? string.Empty) is not string principal)
			return BadRequest(new { error = $"No character or role named {request.Who}." });

		return await casefiles.SetMemberAsync(await ReaderAsync(), key, principal, level) switch
		{
			Casefile casefile => Ok(await ToDtoAsync(casefile)),
			// Saved, and the caller took themselves out: there is no casefile left for them to see.
			Error<string> { Value: CasefileService.LeftMessage } => NoContent(),
			Error<string> error => Refused(error.Value),
		};
	}

	[HttpPost("{key}/developments")]
	public async Task<IActionResult> Post(string key, [FromBody] PostCasefileDevelopmentRequest request)
	{
		if (string.IsNullOrEmpty(CallerDbref)) return Unauthorized("Missing character identity.");
		var author = await Names.NameOfAsync(CallerDbref, HttpContext.RequestAborted) ?? CallerDbref;
		return await casefiles.PostDevelopmentAsync(await ReaderAsync(), key, author, request.Title ?? string.Empty,
			request.Text ?? string.Empty, request.Files ?? []) switch
		{
			WikiPage page => Ok(new { slug = page.Slug, revision = page.RevisionNumber }),
			Error<string> error => Refused(error.Value),
		};
	}

	/// <summary>A refusal the service explained: 404 for a casefile the caller cannot see, 403 for a permission, else 400.</summary>
	private IActionResult Refused(string reason)
		=> reason.StartsWith("No such casefile", StringComparison.Ordinal) ? NotFound(new { error = reason })
			: reason.StartsWith("Permission denied", StringComparison.Ordinal) ? StatusCode(403, new { error = reason })
			: BadRequest(new { error = reason });

	/// <summary><c>role:&lt;slug&gt;</c> as given, an objid, or a character's name as its objid; null when nothing matches.</summary>
	private async Task<string?> PrincipalAsync(string who)
	{
		var trimmed = who.Trim();
		if (trimmed.Length == 0) return null;
		if (trimmed.StartsWith(WikiMember.RolePrefix, StringComparison.OrdinalIgnoreCase)) return trimmed;
		if (DBRef.TryParse(trimmed, out var parsed) && parsed is { IsObjid: true } objid) return objid.ToString();
		await foreach (var player in mediator.CreateStream(new GetPlayerQuery(trimmed.TrimStart('*')))
			.WithCancellation(HttpContext.RequestAborted))
			return player.Object.DBRef.ToString();
		return null;
	}

	private static CasefileSummaryDto ToDto(CasefileSummary summary)
		=> new(summary.Key, summary.Title, summary.Summary, Level(summary.Level), summary.Files, summary.Members, summary.UpdatedAt);

	private async Task<CasefileDto> ToDtoAsync(Casefile casefile)
	{
		var members = new List<CasefileMemberDto>();
		foreach (var member in casefile.Members)
		{
			members.Add(member.RoleSlug is { } slug
				? new CasefileMemberDto(member.Principal, "role", slug, Level(member.Level))
				: new CasefileMemberDto(member.Principal, "character",
					await Names.NameOfAsync(member.Principal, HttpContext.RequestAborted) ?? member.Principal, Level(member.Level)));
		}

		var files = new List<CasefileFileDto>();
		foreach (var page in casefile.Files)
		{
			files.Add(new CasefileFileDto(page.Namespace, page.Slug, page.Title, page.UpdatedAt,
				await Names.NameOfAsync(page.LastEditorDbref, HttpContext.RequestAborted)));
		}

		// Membership never lets anyone write more than wiki.edit does, so a contributor without it gets no write controls.
		var level = casefile.Summary.Level;
		var canWrite = (await ReaderAsync()).Has(PortalPermission.WikiEdit);
		return new CasefileDto(ToDto(casefile.Summary), casefile.Briefing is not null, casefile.Developments?.Slug, files, members,
			canWrite && level >= WikiMemberLevel.Contributor, level >= WikiMemberLevel.Lead);
	}

	private static string Level(WikiMemberLevel level) => level.ToString().ToLowerInvariant();
}
