using System.Globalization;
using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="ICasefileService"/>
public sealed class CasefileService(
	IWikiService wiki,
	IWikiAccessService access,
	IWikiLocalizationService localization,
	IRoleRegistryService roles,
	IMediator mediator) : ICasefileService
{
	/// <summary>The most files a casefile lists.</summary>
	public const int MaxFiles = 500;

	/// <summary>What a member who has just taken themselves out of a casefile is told: saved, and no longer in it.</summary>
	public const string LeftMessage = "Saved. You are no longer a member of the casefile.";

	/// <summary>The suffix of a developments log's title: "Harbour Fire developments".</summary>
	public const string DevelopmentsSuffix = " developments";

	public async Task<IReadOnlyList<CasefileSummary>> ListAsync(WikiReader reader)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		var visibility = await access.VisibilityAsync(reader);
		var found = new List<CasefileSummary>();
		var mine = requirements.Private
			.Where(set => set.Target.Scope == WikiRuleScope.Category)
			.Select(set => (Set: set, Level: LevelFor(reader, set)))
			.Where(entry => entry.Level.HasValue)
			.Select(entry => (entry.Set, Level: entry.Level.GetValueOrDefault()));
		foreach (var (set, level) in mine)
		{
			var (briefing, developments, files) = await PartsAsync(reader, set.Target.Key, visibility);
			found.Add(Summarize(set, level, briefing, developments, files));
		}

		return found.OrderByDescending(summary => summary.UpdatedAt).ThenBy(summary => summary.Title, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public async Task<Found<Casefile>> GetAsync(WikiReader reader, string name)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		if (Find(requirements, name) is not { } set || LevelFor(reader, set) is not { } level) return new NotFound();

		var (briefing, developments, files) = await PartsAsync(reader, set.Target.Key, await access.VisibilityAsync(reader));
		return new Casefile(Summarize(set, level, briefing, developments, files), briefing, developments, files,
			set.Members.OrderByDescending(member => member.Level).ThenBy(member => member.Principal, StringComparer.Ordinal).ToList());
	}

	public async Task<Result<Casefile>> CreateAsync(WikiReader reader, string title, string briefing)
	{
		var trimmed = title.Trim();
		var key = WikiHelpers.CategoryKey(trimmed);
		if (key.Length == 0 || trimmed.Contains('\n') || trimmed.Contains(':'))
			return new Error<string>("A casefile needs a title on one line, without a colon.");
		if (reader.Dbref is not { Length: > 0 } me || !DBRef.TryParse(me, out var creator) || creator is not { IsObjid: true })
			return new Error<string>("Only a character can open a casefile.");
		if (!reader.Has(PortalPermission.WikiCreate))
			return new Error<string>("Permission denied. You need the wiki.create permission.");

		var requirements = await access.RequirementsAsync();
		var target = WikiRuleTarget.ForCategory(key);
		// An outsider learns only that the title is taken, never that a casefile by that name exists.
		if (requirements.For(target) is { IsPrivate: true } taken)
			return new Error<string>(LevelFor(await RolesAsync(reader, requirements), taken) is not null
				? $"There is already a casefile called {trimmed}."
				: "That title is taken. Choose another.");

		// The briefing is a Category page and the log a main one: both creates answer to the wiki's own requirements.
		foreach (var (ns, categories) in new[] { ("category", Array.Empty<string>()), ("main", [key]) })
		{
			var decision = await access.DecideCreateAsync(reader, ns, categories);
			if (!decision.Allowed)
				return new Error<string>($"Permission denied. You can't create pages in {ns}: {decision.Describe()}.");
		}

		var existingPage = Page(await wiki.GetBySlugAsync(key, WikiNamespace.Category));
		var filed = await wiki.CountPagesInCategoryAsync(key, WikiVisibility.All);
		if ((filed > 0 || existingPage is not null) && !reader.Bypasses)
			return new Error<string>(
				$"Permission denied. The category {trimmed} already has pages; making it a casefile needs the wiki.admin permission.");
		// The log's title must be free, or be a page already filed here: posts never go to a page outside the casefile.
		if (Page(await wiki.GetBySlugAsync(DevelopmentsSlug(key))) is { } log && !FiledIn(log, key))
			return new Error<string>("That title is taken. Choose another.");

		// Members first, so the briefing and the log are never readable by everyone, not even for a moment.
		var current = requirements.For(target);
		var set = new WikiRequirementSet(target, current?.Required ?? new Dictionary<WikiAction, IReadOnlyList<string>>(), me,
			DateTimeOffset.UtcNow)
		{ Members = [new WikiMember(me, WikiMemberLevel.Lead)] };
		if (await mediator.Send(new SetWikiRequirementsCommand(set)) is NotFound)
			return new Error<string>("The casefile could not be saved.");

		var opened = await OpenPagesAsync(existingPage, key, trimmed, briefing, me);
		if (opened is Error<string> failed)
		{
			// Nothing was opened: the category goes back to what it was, so its key is not left claimed.
			await mediator.Send(new SetWikiRequirementsCommand(set with { Members = current?.Members ?? [] }));
			return failed;
		}

		return await GetAsync(reader with { Roles = null }, key) switch
		{
			Casefile casefile => casefile,
			_ => new Error<string>("The casefile was opened but could not be read back."),
		};
	}

	/// <summary>The briefing, unless the category already has its page, and the developments log.</summary>
	private async Task<Result<WikiPage>> OpenPagesAsync(WikiPage? existingPage, string key, string title, string briefing,
		string me)
	{
		if (existingPage is null)
		{
			var body = string.IsNullOrWhiteSpace(briefing) ? DefaultBriefing : briefing.Trim();
			if (await wiki.CreateAsync(title, body, me, WikiNamespace.Category, localization.DefaultLocale, slug: key,
				editSummary: "Opened the casefile") is Error<string> error)
				return error;
		}

		return await EnsureDevelopmentsAsync(key, title, me);
	}

	public async Task<bool> TitleTakenAsync(WikiReader reader, WikiNamespace ns, string title)
	{
		if (await wiki.GetBySlugAsync(title, ns) is WikiPage page && !await access.CanSeeAsync(reader, page)) return true;
		if (ns != WikiNamespace.Category || WikiHelpers.CategoryKey(title) is not { Length: > 0 } key) return false;

		// A casefile's briefing, missing or not, is written only by its contributors and leads.
		var requirements = await access.RequirementsAsync();
		return requirements.For(WikiRuleTarget.ForCategory(key)) is { IsPrivate: true } set
			&& LevelFor(await RolesAsync(reader, requirements), set) is not >= WikiMemberLevel.Contributor;
	}

	public async Task<Result<Casefile>> SetMemberAsync(WikiReader reader, string name, string principal, WikiMemberLevel? level)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		if (Find(requirements, name) is not { } set || LevelFor(reader, set) is not { } mine)
			return new Error<string>($"No such casefile: {name.Trim()}.");
		if (mine < WikiMemberLevel.Lead)
			return new Error<string>("Permission denied. Only a lead of the casefile lets people in or out.");

		if (await NormalizeAsync(principal) is not string normalized)
			return new Error<string>($"Not a character or a role: {principal.Trim()}.");

		var others = set.Members.Where(member => !string.Equals(member.Principal, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
		var members = level is { } added ? [.. others, new WikiMember(normalized, added)] : others;
		// A role may have no holders, so only a character counts as the lead that keeps the casefile managed.
		if (!members.Any(member => member.Level == WikiMemberLevel.Lead && member.RoleSlug is null))
			return new Error<string>("A casefile keeps at least one character as its lead. Name another lead first.");
		if (members.Count == set.Members.Count && level is null)
			return new Error<string>("They are not a member of the casefile.");

		var updated = set with { UpdatedBy = reader.Dbref ?? string.Empty, UpdatedAt = DateTimeOffset.UtcNow, Members = members };
		if (await mediator.Send(new SetWikiRequirementsCommand(updated)) is NotFound)
			return new Error<string>("The casefile could not be saved.");

		return await GetAsync(reader, set.Target.Key) switch
		{
			Casefile casefile => casefile,
			// A lead who just stepped down to nothing is no longer in it.
			_ => new Error<string>(LeftMessage),
		};
	}

	public async Task<Result<WikiPage>> PostDevelopmentAsync(WikiReader reader, string name, string authorName, string title,
		string text, IEnumerable<string> files)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		if (Find(requirements, name) is not { } set || LevelFor(reader, set) is not { } level)
			return new Error<string>($"No such casefile: {name.Trim()}.");
		if (level < WikiMemberLevel.Contributor)
			return new Error<string>("Permission denied. Posting a development needs a contributor or lead of the casefile.");
		if (!reader.Has(PortalPermission.WikiEdit))
			return new Error<string>("Permission denied. You need the wiki.edit permission.");
		var heading = title.Trim();
		if (heading.Length == 0 || heading.Contains('\n')) return new Error<string>("A development needs a title on one line.");
		if (string.IsNullOrWhiteSpace(text)) return new Error<string>("A development needs some text.");
		if (reader.Dbref is not { Length: > 0 } me) return new Error<string>("Only a character can post a development.");

		var key = set.Target.Key;
		var caseTitle = Page(await wiki.GetBySlugAsync(key, WikiNamespace.Category))?.Title ?? WikiHelpers.CategoryLabel(key);
		if (await EnsureDevelopmentsAsync(key, caseTitle, me) is not WikiPage log)
			return new Error<string>($"The developments log could not be made: the title {caseTitle}{DevelopmentsSuffix} is used by a page outside the casefile.");
		// The log is a wiki page like any other: a protection or a requirement on it still holds.
		var edit = await access.DecideAsync(reader, log, WikiAction.Edit);
		if (!edit.Allowed)
			return new Error<string>($"Permission denied. {edit.Describe()}.");

		var entry = Entry(DateTimeOffset.UtcNow, heading, authorName, text.Trim(), files);
		var body = string.IsNullOrWhiteSpace(log.MarkdownSource) ? entry : $"{entry}\n\n{log.MarkdownSource.TrimStart()}";
		return await wiki.UpdateAsync(log.Id, body, me, $"Development: {heading}") switch
		{
			WikiPage page => page,
			_ => new Error<string>("The developments log could not be saved."),
		};
	}

	/// <summary>One development as the log holds it: a dated heading, a line naming the author and the files, the text.</summary>
	public static string Entry(DateTimeOffset at, string title, string authorName, string text, IEnumerable<string> files)
	{
		var date = at.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
		var links = files.Select(file => file.Trim()).Where(file => file.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			// Named display text keeps the file's own casing; a bare [[x]] is shown title-cased.
			.Select(file => $"[[{file}|{file}]]")
			.ToList();
		var byline = links.Count == 0
			? $"*Posted by {authorName.Trim()}*"
			: $"*Posted by {authorName.Trim()}. Files: {string.Join(", ", links)}*";
		return $"## {date}: {title}\n\n{byline}\n\n{text}";
	}

	private const string DefaultBriefing =
		"What this case is about, who is involved, and what a newcomer should read first.";

	/// <summary>
	/// The developments log's page, made empty when the casefile has none yet. A page by that title that is not filed
	/// in the casefile is never taken as its log, so a post can never land on a page outsiders read.
	/// </summary>
	private async Task<Result<WikiPage>> EnsureDevelopmentsAsync(string key, string caseTitle, string authorDbref)
	{
		if (await wiki.GetBySlugAsync(DevelopmentsSlug(key)) is WikiPage existing)
			return FiledIn(existing, key)
				? existing
				: new Error<string>($"The page {existing.Title} is not filed in the casefile.");
		return await wiki.CreateAsync(caseTitle + DevelopmentsSuffix, string.Empty, authorDbref, WikiNamespace.Main,
			localization.DefaultLocale, [key], DevelopmentsSlug(key), "Opened the developments log");
	}

	/// <summary>The slug of a casefile's developments log.</summary>
	public static string DevelopmentsSlug(string key) => key + WikiHelpers.Slugify(DevelopmentsSuffix);

	private async Task<(WikiPage? Briefing, WikiPage? Developments, IReadOnlyList<WikiPage> Files)> PartsAsync(WikiReader reader,
		string key, WikiVisibility visibility)
	{
		// The briefing answers to its own requirements and publication like any page: one a member may not read
		// is left out, summary and all.
		var briefing = Page(await wiki.GetBySlugAsync(key, WikiNamespace.Category)) is { } page
			&& await access.CanSeeAsync(reader, page)
				? page
				: null;
		var developmentsSlug = DevelopmentsSlug(key);
		WikiPage? developments = null;
		var files = new List<WikiPage>();
		foreach (var filed in await wiki.GetByCategoryAsync(key, 0, MaxFiles, visibility))
		{
			if (filed.Namespace == "main" && filed.Slug == developmentsSlug) developments = filed;
			else files.Add(filed);
		}

		return (briefing, developments, files.OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase).ToList());
	}

	private static CasefileSummary Summarize(WikiRequirementSet set, WikiMemberLevel level, WikiPage? briefing,
		WikiPage? developments, IReadOnlyList<WikiPage> files)
	{
		var updated = new[] { briefing?.UpdatedAt, developments?.UpdatedAt, set.UpdatedAt }
			.Concat(files.Select(file => (DateTimeOffset?)file.UpdatedAt))
			.Max() ?? set.UpdatedAt;
		return new CasefileSummary(set.Target.Key, briefing?.Title ?? WikiHelpers.CategoryLabel(set.Target.Key),
			FirstParagraph(briefing?.PlainText), level, files.Count, set.Members.Count, updated);
	}

	private static string FirstParagraph(string? text)
	{
		var paragraph = (text ?? string.Empty).Trim().Split("\n\n", 2)[0].ReplaceLineEndings(" ").Trim();
		return paragraph.Length <= 200 ? paragraph : paragraph[..199].TrimEnd() + "…";
	}

	private static WikiPage? Page(Found<WikiPage> found) => found is WikiPage page ? page : null;

	private static bool FiledIn(WikiPage page, string key)
		=> page.Categories.Any(category => string.Equals(WikiHelpers.CategoryKey(category), key, StringComparison.OrdinalIgnoreCase));

	private static WikiRequirementSet? Find(WikiRequirements requirements, string name)
		=> WikiHelpers.CategoryKey(name) is { Length: > 0 } key
			&& requirements.For(WikiRuleTarget.ForCategory(key)) is { IsPrivate: true } set
				? set
				: null;

	/// <summary>The reader's level, or <see cref="WikiMemberLevel.Lead"/> for a <c>wiki.admin</c> holder; null for an outsider.</summary>
	private static WikiMemberLevel? LevelFor(WikiReader reader, WikiRequirementSet set)
		=> reader.Bypasses ? WikiMemberLevel.Lead : set.LevelOf(reader.Dbref, reader.Roles ?? new HashSet<string>());

	private async Task<WikiReader> RolesAsync(WikiReader reader, WikiRequirements requirements)
		=> access is WikiAccessService service ? await service.WithRolesAsync(reader, requirements) : reader;

	/// <summary>An objid as stored, or <c>role:&lt;slug&gt;</c> for a role that exists; null for anything else.</summary>
	private async Task<string?> NormalizeAsync(string principal)
	{
		var trimmed = principal.Trim();
		if (trimmed.StartsWith(WikiMember.RolePrefix, StringComparison.OrdinalIgnoreCase))
		{
			var slug = trimmed[WikiMember.RolePrefix.Length..].Trim().ToLowerInvariant();
			return slug.Length > 0 && await roles.GetRoleAsync(slug) is SharpRole ? WikiMember.ForRole(slug) : null;
		}

		return DBRef.TryParse(trimmed, out var parsed) && parsed is { IsObjid: true } objid ? objid.ToString() : null;
	}
}
