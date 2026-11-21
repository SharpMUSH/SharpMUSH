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
	/// <summary>The category every casefile is filed in.</summary>
	public const string Category = "Case Files";

	/// <summary>The heading developments are posted under, newest first.</summary>
	public const string DevelopmentsHeading = "## Developments";

	/// <summary>What a member who has just taken themselves out of a casefile is told: saved, and no longer in it.</summary>
	public const string LeftMessage = "Saved. You are no longer a member of the case file.";

	public async Task<IReadOnlyList<CasefileSummary>> ListAsync(WikiReader reader)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		var found = new List<CasefileSummary>();
		var mine = requirements.Private
			.Where(set => set.Target.Scope == WikiRuleScope.Page)
			.Select(set => (Set: set, Level: LevelFor(reader, set)))
			.Where(entry => entry.Level.HasValue)
			.Select(entry => (entry.Set, Level: entry.Level.GetValueOrDefault()));
		foreach (var (set, level) in mine)
		{
			// The page answers to its own publication and requirements like any page.
			if (await wiki.GetByIdAsync(set.Target.Key) is WikiPage page && await access.CanSeeAsync(reader, page))
				found.Add(Summarize(set, level, page));
		}

		return found.OrderByDescending(summary => summary.UpdatedAt).ThenBy(summary => summary.Title, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public async Task<Found<Casefile>> GetAsync(WikiReader reader, string name)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		return await FindAsync(requirements, name) is ({ } page, { } set)
			&& LevelFor(reader, set) is { } level
			&& await access.CanSeeAsync(reader, page)
				? new Casefile(Summarize(set, level, page), page,
					set.Members.OrderByDescending(member => member.Level).ThenBy(member => member.Principal, StringComparer.Ordinal).ToList())
				: new NotFound();
	}

	public async Task<Result<Casefile>> CreateAsync(WikiReader reader, string title, string briefing)
	{
		var trimmed = title.Trim();
		if (WikiHelpers.Slugify(trimmed).Length == 0 || trimmed.Contains('\n') || trimmed.Contains(':'))
			return new Error<string>("A case file needs a title on one line, without a colon.");
		if (reader.Dbref is not { Length: > 0 } me || !DBRef.TryParse(me, out var creator) || creator is not { IsObjid: true })
			return new Error<string>("Only a character can open a case file.");
		if (!reader.Has(PortalPermission.WikiCreate))
			return new Error<string>("Permission denied. You need the wiki.create permission.");

		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		if (await wiki.GetBySlugAsync(WikiHelpers.Slugify(trimmed)) is WikiPage existing)
			return new Error<string>(await TakenAsync(reader, requirements, existing, trimmed));

		var decision = await access.DecideCreateAsync(reader, "main", [Category]);
		if (!decision.Allowed)
			return new Error<string>($"Permission denied. You can't create pages in {Category}: {decision.Describe()}.");

		var body = string.IsNullOrWhiteSpace(briefing) ? DefaultBriefing : briefing.Trim();
		if (await wiki.CreateAsync(trimmed, body, me, WikiNamespace.Main, localization.DefaultLocale, [Category],
			editSummary: "Opened the case file") is not WikiPage page)
			return new Error<string>("That title is taken. Choose another.");

		// The page's id is its key, so the member list follows the create at once.
		var set = new WikiRequirementSet(WikiRuleTarget.ForPage(page.Id), new Dictionary<WikiAction, IReadOnlyList<string>>(), me,
			DateTimeOffset.UtcNow)
		{ Members = [new WikiMember(me, WikiMemberLevel.Lead)] };
		if (await mediator.Send(new SetWikiRequirementsCommand(set)) is NotFound)
		{
			// A casefile nobody is in would be a public page: take it back.
			await wiki.DeleteAsync(page.Id, me);
			return new Error<string>("The case file could not be saved.");
		}

		return await GetAsync(reader with { Roles = null }, page.Slug) switch
		{
			Casefile casefile => casefile,
			_ => new Error<string>("The case file was opened but could not be read back."),
		};
	}

	/// <summary>Why a title is not free: a member hears that it is their casefile, anyone who can see the page that it exists, anyone else only that it is taken.</summary>
	private async Task<string> TakenAsync(WikiReader reader, WikiRequirements requirements, WikiPage existing, string title)
	{
		if (!await access.CanSeeAsync(reader, existing)) return "That title is taken. Choose another.";
		return requirements.For(WikiRuleTarget.ForPage(existing.Id)) is { IsPrivate: true }
			? $"There is already a case file called {existing.Title}."
			: $"There is already a page called {title}. Choose another title.";
	}

	public async Task<bool> TitleTakenAsync(WikiReader reader, WikiNamespace ns, string title)
		=> await wiki.GetBySlugAsync(title, ns) is WikiPage page && !await access.CanSeeAsync(reader, page);

	public async Task<Result<Casefile>> SetMemberAsync(WikiReader reader, string name, string principal, WikiMemberLevel? level)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		if (await FindAsync(requirements, name) is not ({ } page, { } set) || LevelFor(reader, set) is not { } mine)
			return new Error<string>($"No such case file: {name.Trim()}.");
		if (mine < WikiMemberLevel.Lead)
			return new Error<string>("Permission denied. Only a lead of the case file lets people in or out.");

		if (await NormalizeAsync(principal) is not string normalized)
			return new Error<string>($"Not a character or a role: {principal.Trim()}.");

		var others = set.Members.Where(member => !string.Equals(member.Principal, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
		var members = level is { } added ? [.. others, new WikiMember(normalized, added)] : others;
		// A role may have no holders, so only a character counts as the lead that keeps the casefile managed.
		if (!members.Any(member => member.Level == WikiMemberLevel.Lead && member.RoleSlug is null))
			return new Error<string>("A case file keeps at least one character as its lead. Name another lead first.");
		if (members.Count == set.Members.Count && level is null)
			return new Error<string>("They are not a member of the case file.");

		var updated = set with { UpdatedBy = reader.Dbref ?? string.Empty, UpdatedAt = DateTimeOffset.UtcNow, Members = members };
		if (await mediator.Send(new SetWikiRequirementsCommand(updated)) is NotFound)
			return new Error<string>("The case file could not be saved.");

		return await GetAsync(reader, page.Slug) switch
		{
			Casefile casefile => casefile,
			// A lead who just stepped down to nothing is no longer in it.
			_ => new Error<string>(LeftMessage),
		};
	}

	public async Task<Result<WikiPage>> PostDevelopmentAsync(WikiReader reader, string name, string authorName, string title,
		string text)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		if (await FindAsync(requirements, name) is not ({ } page, { } set) || LevelFor(reader, set) is not { } level
			|| !await access.CanSeeAsync(reader, page))
			return new Error<string>($"No such case file: {name.Trim()}.");
		if (level < WikiMemberLevel.Contributor)
			return new Error<string>("Permission denied. Posting a development needs a contributor or lead of the case file.");
		// The page is a wiki page like any other: wiki.edit, a protection or a requirement on it still holds.
		var edit = await access.DecideAsync(reader, page, WikiAction.Edit);
		if (!edit.Allowed)
			return new Error<string>(edit.MissingScope is { } scope && edit.RequiredBy is null
				? $"Permission denied. You need the {scope} permission."
				: $"Permission denied. {Sentence(edit.Describe())}.");
		var heading = title.Trim();
		if (heading.Length == 0 || heading.Contains('\n')) return new Error<string>("A development needs a title on one line.");
		if (string.IsNullOrWhiteSpace(text)) return new Error<string>("A development needs some text.");
		if (reader.Dbref is not { Length: > 0 } me) return new Error<string>("Only a character can post a development.");

		var entry = Entry(DateTimeOffset.UtcNow, heading, authorName, text.Trim());
		return await wiki.UpdateAsync(page.Id, WithDevelopment(page.MarkdownSource, entry), me, $"Development: {heading}") switch
		{
			WikiPage updated => updated,
			_ => new Error<string>("The case file could not be saved."),
		};
	}

	/// <summary>
	/// <paramref name="markdown"/> with <paramref name="entry"/> first under its Developments heading, which is added at
	/// the end when the page has none.
	/// </summary>
	public static string WithDevelopment(string markdown, string entry)
	{
		var lines = markdown.ReplaceLineEndings("\n").TrimEnd().Split('\n').ToList();
		var at = lines.FindIndex(line => string.Equals(line.Trim(), DevelopmentsHeading, StringComparison.OrdinalIgnoreCase));
		if (at < 0)
			return $"{string.Join('\n', lines).TrimEnd()}\n\n{DevelopmentsHeading}\n\n{entry}\n";

		var after = string.Join('\n', lines.Skip(at + 1)).Trim();
		var before = string.Join('\n', lines.Take(at + 1));
		return after.Length == 0 ? $"{before}\n\n{entry}\n" : $"{before}\n\n{entry}\n\n{after}\n";
	}

	/// <summary>One development as the page holds it: a dated heading, a line naming the author, the text.</summary>
	public static string Entry(DateTimeOffset at, string title, string authorName, string text)
	{
		var date = at.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
		return $"### {date}: {title}\n\n*Posted by {authorName.Trim()}*\n\n{text}";
	}

	private static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

	private const string DefaultBriefing =
		"What this case is about, who is involved, and what a newcomer should read first.";

	private static CasefileSummary Summarize(WikiRequirementSet set, WikiMemberLevel level, WikiPage page)
		=> new(page.Slug, page.Title, FirstParagraph(page.PlainText), level, set.Members.Count,
			page.UpdatedAt > set.UpdatedAt ? page.UpdatedAt : set.UpdatedAt);

	private static string FirstParagraph(string? text)
	{
		var paragraph = (text ?? string.Empty).Trim().Split("\n\n", 2)[0].ReplaceLineEndings(" ").Trim();
		return paragraph.Length <= 200 ? paragraph : paragraph[..199].TrimEnd() + "…";
	}

	/// <summary>The page <paramref name="name"/> names in the main namespace and its member list, when it is a casefile.</summary>
	private async Task<(WikiPage Page, WikiRequirementSet Set)?> FindAsync(WikiRequirements requirements, string name)
		=> WikiHelpers.Slugify(name.Trim()) is { Length: > 0 } slug
			&& await wiki.GetBySlugAsync(slug) is WikiPage page
			&& requirements.For(WikiRuleTarget.ForPage(page.Id)) is { IsPrivate: true } set
				? (page, set)
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
