using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Logging;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// What namespaces, categories and pages require of a reader, the portal's face of <c>@wiki/require</c> and
/// <c>@wiki/access</c>.
///
/// Routes:
///   GET /api/wiki/requirements                — every requirement set (wiki.admin)
///   GET /api/wiki/requirements/{scope}/{key}  — one namespace's, category's or page's set
///   PUT /api/wiki/requirements/{scope}/{key}  — change it (wiki.admin)
///   GET /api/wiki/{slug}/requirements?ns=     — a page's own set and what it inherits
///   GET /api/wiki/{slug}/access?ns=           — what the caller may do with a page, and why
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiRequirementsController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	IPrerenderCacheService prerenderCache,
	IWikiNameResolver names,
	ILogger<WikiRequirementsController> logger) : WikiControllerBase(wikiService, localization, access, names, logger)
{
	[HttpGet("requirements")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> GetAll()
	{
		var sets = new List<WikiRequirementSetDto>();
		foreach (var set in (await Access.RequirementsAsync()).Sets)
		{
			sets.Add(await ToDtoAsync(set.Target, set));
		}

		return Ok(sets);
	}

	[HttpGet("requirements/{scope}/{key}")]
	public async Task<IActionResult> Get(string scope, string key)
	{
		if (ParseTarget(scope, key) is not { } target)
			return NotFound();
		// A page answers to whoever may see it; a namespace or category to whoever may read the wiki at all.
		if (target.Scope == WikiRuleScope.Page
			? await Wiki.GetByIdAsync(target.Key) is not WikiPage page || !await CanSeeAsync(page)
			: !(await ReaderAsync()).Has(PortalPermission.WikiRead))
			return NotFound();

		return Ok(await ToDtoAsync(target, (await Access.RequirementsAsync()).For(target)));
	}

	[HttpPut("requirements/{scope}/{key}")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> Put(string scope, string key, [FromBody] SetRequirementsRequest request)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		if (ParseTarget(scope, key) is not { } target)
			return NotFound();

		var changes = new Dictionary<WikiAction, IReadOnlyList<string>>();
		foreach (var (actionName, scopes) in request.Required ?? new Dictionary<string, IReadOnlyList<string>>())
		{
			if (!Enum.TryParse<WikiAction>(actionName, ignoreCase: true, out var action) || int.TryParse(actionName, out _))
				return BadRequest(new { error = $"No such action: {actionName}. Actions are read, create, edit and delete." });
			changes[action] = scopes ?? [];
		}

		var result = await Access.SetRequirementsAsync(await ReaderAsync(), editorDbref, target, changes);
		switch (result)
		{
			case WikiRequirements requirements:
				Logger.LogInformation("Wiki requirements set: target={Target} by={Editor}",
					LogSanitizer.Sanitize(target.ToString()), LogSanitizer.Sanitize(editorDbref));
				// A read requirement changes what a crawler may see.
				prerenderCache.InvalidatePrefix("/wiki/");
				prerenderCache.InvalidatePrefix("/character/");
				return Ok(await ToDtoAsync(target, requirements.For(target)));
			case Error<string> error:
				return BadRequest(new { error = error.Value });
			default:
				return NotFound();
		}
	}

	[HttpGet("{slug}/requirements")]
	public async Task<IActionResult> GetForPage(string slug, [FromQuery] string? ns = null)
	{
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page || !await CanSeeAsync(page))
			return NotFound();

		var requirements = await Access.RequirementsAsync();
		var inherited = await page.Categories.Select(WikiRuleTarget.ForCategory)
			.Prepend(WikiRuleTarget.ForNamespace(page.Namespace))
			.Where(target => requirements.For(target) is not null)
			.ToAsyncEnumerable()
			.Select(async (WikiRuleTarget target, CancellationToken _) => await ToDtoAsync(target, requirements.For(target)))
			.ToListAsync();

		var self = WikiRuleTarget.ForPage(page.Id);
		return Ok(new WikiPageRequirementsDto(await ToDtoAsync(self, requirements.For(self), page.Title), inherited));
	}

	[HttpGet("{slug}/access")]
	public async Task<IActionResult> GetAccess(string slug, [FromQuery] string? ns = null)
	{
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page || !await CanSeeAsync(page))
			return NotFound();

		var reader = await ReaderAsync();
		var answers = new List<WikiAccessExplanationDto>();
		foreach (var action in Enum.GetValues<WikiAction>().Where(action => action != WikiAction.Create))
		{
			var decision = await Access.DecideAsync(reader, page, action);
			answers.Add(new WikiAccessExplanationDto(action.ToString().ToLowerInvariant(), decision.Allowed, decision.Describe()));
		}

		return Ok(answers);
	}

	/// <summary>A target from the route, or null when the scope is unknown or names no namespace.</summary>
	private static WikiRuleTarget? ParseTarget(string scope, string key)
	{
		if (!Enum.TryParse<WikiRuleScope>(scope, ignoreCase: true, out var parsed) || int.TryParse(scope, out _)
			|| string.IsNullOrWhiteSpace(key))
			return null;

		return parsed switch
		{
			WikiRuleScope.Namespace => WikiHelpers.ParseNamespace(key) is { } ns ? WikiRuleTarget.ForNamespace(ns) : null,
			WikiRuleScope.Category => WikiHelpers.CategoryKey(key) is { Length: > 0 } category ? WikiRuleTarget.ForCategory(category) : null,
			_ => WikiRuleTarget.ForPage(key),
		};
	}

	private async Task<WikiRequirementSetDto> ToDtoAsync(WikiRuleTarget target, WikiRequirementSet? set, string? label = null)
	{
		label ??= target.Scope switch
		{
			WikiRuleScope.Page => await Wiki.GetByIdAsync(target.Key) is WikiPage page ? page.Title : target.Key,
			WikiRuleScope.Namespace => WikiHelpers.ParseNamespace(target.Key) is { } ns ? ns.ToString() : target.Key,
			_ => target.Key,
		};

		var required = Enum.GetValues<WikiAction>()
			.Where(action => set?.For(action).Count > 0)
			.ToDictionary(action => action.ToString().ToLowerInvariant(), action => set!.For(action));
		return new WikiRequirementSetDto(
			target.Scope.ToString().ToLowerInvariant(), target.Key, label, required,
			set is null ? null : await Names.NameOfAsync(set.UpdatedBy, HttpContext.RequestAborted),
			set?.UpdatedAt);
	}
}
