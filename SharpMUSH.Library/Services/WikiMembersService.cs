using Mediator;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IWikiMembersService"/>
public sealed class WikiMembersService(
	IWikiService wiki,
	IWikiAccessService access,
	IWikiLocalizationService localization,
	IRoleRegistryService roles,
	IMediator mediator) : IWikiMembersService
{
	/// <summary>What a member who has just taken themselves out of a page is told: saved, and no longer in it.</summary>
	public const string LeftMessage = "Saved. You are no longer a member of the page.";

	/// <summary>
	/// One member change at a time, from its read to its write, so two changes never write member lists made
	/// from the same stale read (the engine runs in one process).
	/// </summary>
	private readonly SemaphoreSlim _memberChanges = new(1, 1);

	public async Task<Found<WikiPageMembers>> GetAsync(WikiReader reader, WikiPage page)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		return requirements.For(WikiRuleTarget.ForPage(page.Id)) is { IsPrivate: true } set
			&& LevelFor(reader, set) is { } level
			&& await access.CanSeeAsync(reader, page)
				? Describe(page, set, level)
				: new NotFound();
	}

	public async Task<Result<WikiPage>> CreatePrivateAsync(WikiReader reader, WikiNamespace ns, string title, string markdown)
	{
		var trimmed = title.Trim();
		if (WikiHelpers.Slugify(trimmed).Length == 0 || trimmed.Contains('\n'))
			return new Error<string>("A page needs a title on one line.");
		if (reader.Dbref is not { Length: > 0 } me || !DBRef.TryParse(me, out var creator) || creator is not { IsObjid: true })
			return new Error<string>("Only a character can be the lead of a private page.");

		var decision = await access.DecideCreateAsync(reader, WikiHelpers.NamespaceName(ns), trimmed, []);
		if (!decision.Allowed)
			return new Error<string>($"You can't create pages in {WikiHelpers.NamespaceName(ns)}: {decision.Describe()}.");
		if (await wiki.GetBySlugAsync(WikiHelpers.Slugify(trimmed), ns) is WikiPage existing)
			return new Error<string>(await access.CanSeeAsync(reader, existing)
				? $"There is already a page called {existing.Title}."
				: "That title is taken. Choose another.");

		if (await wiki.CreateAsync(trimmed, markdown, me, ns, localization.DefaultLocale) is not WikiPage page)
			return new Error<string>("That title is taken. Choose another.");

		// The page's id is its key, so the member list follows the create at once.
		var set = new WikiRequirementSet(WikiRuleTarget.ForPage(page.Id), new Dictionary<WikiAction, IReadOnlyList<string>>(), me,
			DateTimeOffset.UtcNow)
		{ Members = [new WikiMember(me, WikiMemberLevel.Lead)], Keep = WikiRequirementKeep.Required };
		// A private page nobody is in would be a public page: whatever stops the member list, take the page back.
		var saved = false;
		try
		{
			saved = await mediator.Send(new SetWikiRequirementsCommand(set)) is None;
		}
		finally
		{
			if (!saved) await wiki.DeleteAsync(page.Id, me);
		}

		return saved ? page : new Error<string>("The page could not be saved.");
	}

	public async Task<bool> TitleTakenAsync(WikiReader reader, WikiNamespace ns, string title)
		=> await wiki.GetBySlugAsync(title, ns) is WikiPage page && !await access.CanSeeAsync(reader, page);

	public async Task<Result<WikiPageMembers>> SetMemberAsync(WikiReader reader, WikiPage page, string principal, WikiMemberLevel? level)
	{
		await _memberChanges.WaitAsync();
		try
		{
			return await ChangeMemberAsync(reader, page, principal, level);
		}
		finally
		{
			_memberChanges.Release();
		}
	}

	private async Task<Result<WikiPageMembers>> ChangeMemberAsync(WikiReader reader, WikiPage page, string principal,
		WikiMemberLevel? level)
	{
		var requirements = await access.RequirementsAsync();
		reader = await RolesAsync(reader, requirements);
		var target = WikiRuleTarget.ForPage(page.Id);
		var existing = requirements.For(target);
		var set = existing ?? new WikiRequirementSet(target, new Dictionary<WikiAction, IReadOnlyList<string>>(),
			reader.Dbref ?? string.Empty, DateTimeOffset.UtcNow);
		if (!set.IsPrivate && !reader.Bypasses)
			return new Error<string>("Permission denied. Only wiki.admin makes a public page private.");
		if (set.IsPrivate && (LevelFor(reader, set) is null || !await access.CanSeeAsync(reader, page)))
			return new Error<string>("No such page.");
		if (set.IsPrivate && LevelFor(reader, set) < WikiMemberLevel.Lead)
			return new Error<string>("Permission denied. Only a lead of the page lets people in or out.");

		if (await NormalizeAsync(principal) is not string normalized)
			return new Error<string>($"Not a character or a role: {principal.Trim()}.");

		var others = set.Members.Where(member => !string.Equals(member.Principal, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
		var members = level is { } added ? [.. others, new WikiMember(normalized, added)] : others;
		if (members.Count == set.Members.Count && level is null)
			return new Error<string>("They are not a member of the page.");
		// A role may have no holders, so only a character counts as the lead that keeps the page managed.
		if (!members.Any(member => member.Level == WikiMemberLevel.Lead && member.RoleSlug is null))
			return new Error<string>("A private page keeps at least one character as its lead. Name another lead first.");

		var updated = set with
		{
			UpdatedBy = reader.Dbref ?? string.Empty,
			UpdatedAt = DateTimeOffset.UtcNow,
			Members = members,
			Keep = WikiRequirementKeep.Required,
		};
		if (await mediator.Send(new SetWikiRequirementsCommand(updated)) is NotFound)
			return new Error<string>("The page could not be saved.");

		return await GetAsync(reader with { Roles = null }, page) switch
		{
			WikiPageMembers members1 => members1,
			// A lead who just stepped down to nothing is no longer in it.
			_ => new Error<string>(LeftMessage),
		};
	}

	private static WikiPageMembers Describe(WikiPage page, WikiRequirementSet set, WikiMemberLevel level)
		=> new(page,
			set.Members.OrderByDescending(member => member.Level).ThenBy(member => member.Principal, StringComparer.Ordinal).ToList(),
			level);

	/// <summary>The reader's level, or <see cref="WikiMemberLevel.Lead"/> for a <c>wiki.admin</c> holder; null for an outsider.</summary>
	private static WikiMemberLevel? LevelFor(WikiReader reader, WikiRequirementSet set)
		=> reader.Bypasses ? WikiMemberLevel.Lead : set.LevelOf(reader.Dbref, reader.Roles ?? new HashSet<string>());

	private async Task<WikiReader> RolesAsync(WikiReader reader, WikiRequirements requirements)
		=> access is WikiAccessService service ? await service.WithRolesAsync(reader, requirements) : reader;

	/// <summary>A player's objid as stored, or <c>role:&lt;slug&gt;</c> for a role that exists; null for anything else.</summary>
	private async Task<string?> NormalizeAsync(string principal)
	{
		var trimmed = principal.Trim();
		if (trimmed.StartsWith(WikiMember.RolePrefix, StringComparison.OrdinalIgnoreCase))
		{
			var slug = trimmed[WikiMember.RolePrefix.Length..].Trim().ToLowerInvariant();
			return slug.Length > 0 && await roles.GetRoleAsync(slug) is SharpRole ? WikiMember.ForRole(slug) : null;
		}

		return DBRef.TryParse(trimmed, out var parsed) && parsed is { IsObjid: true } objid
			&& await mediator.Send(new GetObjectNodeQuery(objid)) is AnySharpObject and SharpPlayer
				? objid.ToString()
				: null;
	}
}
