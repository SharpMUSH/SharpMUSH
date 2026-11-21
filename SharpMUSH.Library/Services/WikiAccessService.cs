using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IWikiAccessService"/>
public sealed class WikiAccessService(
	IWikiService wiki,
	IRoleRegistryService roles,
	IPermissionResolver resolver,
	ICharacterBiographyControl biographies,
	IWikiReaderRoles? readerRoles) : IWikiAccessService
{
	/// <summary>
	/// The plugin contract's constructor. Without a game to ask, no one but a <c>wiki.admin</c> holder writes
	/// a character's biography, and a reader's roles are known only when the reader carries them.
	/// </summary>
	public WikiAccessService(IWikiService wiki, IRoleRegistryService roles, IPermissionResolver resolver)
		: this(wiki, roles, resolver, NoBiographyWriters.Instance, null)
	{
	}

	/// <summary>An access service that knows a reader's roles only when the reader carries them.</summary>
	public WikiAccessService(IWikiService wiki, IRoleRegistryService roles, IPermissionResolver resolver,
		ICharacterBiographyControl biographies)
		: this(wiki, roles, resolver, biographies, null)
	{
	}

	private sealed class NoBiographyWriters : ICharacterBiographyControl
	{
		public static readonly NoBiographyWriters Instance = new();

		public ValueTask<bool> MayWriteAsync(string? writerDbref, string title, string slug, CancellationToken ct = default)
			=> ValueTask.FromResult(false);
	}

	/// <summary>Why <see cref="BiographyRule"/> refuses: the page is somebody else's biography.</summary>
	internal const string OthersBiography = "it is another character's biography";

	public async ValueTask<WikiReader> AnonymousAsync(CancellationToken ct = default)
	{
		var everyone = (await roles.GetRolesAsync(ct)).Where(BuiltInRoles.IsEveryone).ToArray();
		var custom = await roles.GetCustomPermissionsAsync(ct);
		var context = new PermissionContext(everyone, new Dictionary<string, PermissionState>(), false)
		{
			CustomScopes = custom.Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase)
		};
		return WikiReader.From(resolver.Resolve(context), null) with
		{
			Roles = everyone.Select(role => role.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase)
		};
	}

	public async ValueTask<WikiReader> ForObjectAsync(AnySharpObject obj, CancellationToken ct = default)
	{
		var grants = await obj.GrantsAsync(ct);
		return WikiReader.From(grants.Granted, obj.Object().DBRef.ToString()) with { Roles = RoleSlugs(grants) };
	}

	/// <summary>The slugs of the roles <paramref name="grants"/> hold.</summary>
	public static IReadOnlySet<string> RoleSlugs(ObjectGrants grants)
		=> grants.Roles.Select(held => held.Role.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// <paramref name="reader"/> with its roles filled in when a private area names a role and they are not
	/// known yet; a reader with no dbref, or no way to look them up, holds none.
	/// </summary>
	public async ValueTask<WikiReader> WithRolesAsync(WikiReader reader, WikiRequirements requirements,
		CancellationToken ct = default)
	{
		if (reader.Roles is not null) return reader;
		var named = requirements.Private.Any(set => set.Members.Any(member => member.RoleSlug is not null));
		if (!named || reader.Dbref is not { Length: > 0 } dbref || readerRoles is null)
			return reader with { Roles = new HashSet<string>() };
		return reader with { Roles = await readerRoles.RolesOfAsync(dbref, ct) };
	}

	public Task<WikiRequirements> RequirementsAsync() => wiki.GetRequirementsAsync();

	public async Task<WikiDecision> DecideAsync(WikiReader reader, WikiPage page, WikiAction action)
	{
		var requirements = await RequirementsAsync();
		return await BiographyRule(reader, page.Namespace, page.Title, page.Slug,
			Decide(await WithRolesAsync(reader, requirements), requirements, page, action));
	}

	public bool MaySeeDraft(WikiReader reader, WikiPage page)
		=> page.Published || reader.Has(PortalPermission.WikiDrafts) || IsAuthor(reader, page);

	public async Task<WikiDecision> DecideCreateAsync(WikiReader reader, string ns, IEnumerable<string> categories)
	{
		var requirements = await RequirementsAsync();
		var lowered = ns.ToLowerInvariant();
		var filed = WikiHelpers.NormalizeCategories(categories, WikiHelpers.ParseNamespace(ns));
		return Decide(await WithRolesAsync(reader, requirements), requirements, lowered, filed, null, WikiAction.Create);
	}

	public async Task<WikiDecision> DecideCreateAsync(WikiReader reader, string ns, string title, IEnumerable<string> categories)
		=> await BiographyRule(reader, ns.ToLowerInvariant(), title, WikiHelpers.Slugify(title),
			await DecideCreateAsync(reader, ns, categories));

	public async Task<WikiDecision> DecideCategoriesAsync(WikiReader reader, WikiPage page, IEnumerable<string> categories)
	{
		var requirements = await RequirementsAsync();
		reader = await WithRolesAsync(reader, requirements);
		var before = await BiographyRule(reader, page.Namespace, page.Title, page.Slug,
			Decide(reader, requirements, page, WikiAction.Edit));
		return before.Allowed
			? Decide(reader, requirements, page.Namespace,
				WikiHelpers.NormalizeCategories(categories, WikiHelpers.ParseNamespace(page.Namespace)), page.Id,
				WikiAction.Edit)
			: before;
	}

	public async Task<WikiAccess> ForPageAsync(WikiReader reader, WikiPage page)
	{
		var read = await CanSeeAsync(reader, page);
		return new WikiAccess(
			read,
			read && (await DecideAsync(reader, page, WikiAction.Edit)).Allowed,
			read && (await DecideAsync(reader, page, WikiAction.Delete)).Allowed,
			reader.Has(PortalPermission.WikiAdmin));
	}

	public async Task<bool> CanSeeAsync(WikiReader reader, WikiPage page)
		=> MaySeeDraft(reader, page) && (await DecideAsync(reader, page, WikiAction.Read)).Allowed;

	public async Task<WikiVisibility> VisibilityAsync(WikiReader reader)
	{
		var visibility = new WikiVisibility(reader.Has(PortalPermission.WikiDrafts), reader.Dbref);
		if (!reader.Has(PortalPermission.WikiRead)) return visibility with { Hidden = WikiReadRestrictions.All };
		if (reader.Bypasses) return visibility;

		var requirements = await RequirementsAsync();
		reader = await WithRolesAsync(reader, requirements);
		var hidden = requirements.Sets
			.Where(set => set.For(WikiAction.Read).Any(scope => !reader.Has(scope))
				|| (set.IsPrivate && set.LevelOf(reader.Dbref, reader.Roles!) is null))
			.Select(set => set.Target)
			.ToList();
		if (hidden.Count == 0) return visibility;

		HashSet<string> Keys(WikiRuleScope scope)
			=> hidden.Where(t => t.Scope == scope).Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

		return visibility with
		{
			Hidden = new WikiReadRestrictions(false, Keys(WikiRuleScope.Namespace), Keys(WikiRuleScope.Category),
				Keys(WikiRuleScope.Page))
		};
	}

	public async Task<FoundResult<WikiRequirements>> SetRequirementsAsync(WikiReader actor, string actorDbref,
		WikiRuleTarget target, IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> changes)
	{
		if (!actor.Has(PortalPermission.WikiAdmin))
			return new Error<string>("Setting wiki requirements needs the wiki.admin permission.");

		var custom = (await roles.GetCustomPermissionsAsync()).Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var unknown = changes.Values.SelectMany(scopes => scopes)
			.Where(scope => !PortalPermission.IsKnown(scope) && !custom.Contains(scope))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (unknown.Count > 0)
			return new Error<string>($"No such permission: {string.Join(", ", unknown)}.");

		var current = (await RequirementsAsync()).For(target)?.Required ?? new Dictionary<WikiAction, IReadOnlyList<string>>();
		var merged = Enum.GetValues<WikiAction>()
			.ToDictionary(action => action,
				action => changes.TryGetValue(action, out var replaced) ? replaced : current.GetValueOrDefault(action, []));

		return await wiki.SetRequirementsAsync(target, merged, actorDbref) switch
		{
			None => await RequirementsAsync(),
			NotFound notFound => notFound,
		};
	}

	/// <summary>
	/// A biography is its character's: past the permissions, writing one (creating, editing, deleting) also
	/// needs the reader to be that character's player or to control the character. <c>wiki.admin</c> skips this
	/// as it skips every requirement; reading is never affected.
	/// </summary>
	private async ValueTask<WikiDecision> BiographyRule(WikiReader reader, string ns, string title, string slug, WikiDecision decision)
		=> !decision.Allowed || decision.Bypassed || decision.Action == WikiAction.Read
			|| WikiHelpers.ParseNamespace(ns) is not WikiNamespace.Character
			|| await biographies.MayWriteAsync(reader.Dbref, title, slug)
				? decision
				: new WikiDecision(decision.Action, false, false, null, null) { Reason = OthersBiography };

	private static bool IsAuthor(WikiReader reader, WikiPage page)
		=> reader.Dbref is { Length: > 0 } me && string.Equals(page.AuthorDbref, me, StringComparison.Ordinal);

	internal static WikiDecision Decide(WikiReader reader, WikiRequirements requirements, WikiPage page, WikiAction action)
		=> Decide(reader, requirements, page.Namespace, page.Categories, page.Id, action);

	/// <summary>
	/// The global scope for the action (and for reading), then, unless the reader bypasses them, every requirement,
	/// then membership of every private area the page sits in: any level to read, a contributor or lead to write.
	/// </summary>
	internal static WikiDecision Decide(WikiReader reader, WikiRequirements requirements, string ns,
		IEnumerable<string> categories, string? pageId, WikiAction action)
	{
		var filed = categories as IReadOnlyCollection<string> ?? categories.ToList();
		foreach (var global in new[] { WikiRequirements.GlobalScope(WikiAction.Read), WikiRequirements.GlobalScope(action) }.Distinct())
		{
			if (!reader.Has(global)) return new WikiDecision(action, false, false, global, null);
		}

		if (reader.Bypasses) return new WikiDecision(action, true, true, null, null);

		foreach (var requirement in requirements.Required(ns, filed, pageId, action))
		{
			if (!reader.Has(requirement.Scope))
				return new WikiDecision(action, false, false, requirement.Scope, requirement.Source);
		}

		var needed = WikiMember.Needed(action);
		var roles = reader.Roles ?? new HashSet<string>();
		foreach (var set in requirements.PrivateSets(ns, filed, pageId))
		{
			var level = set.LevelOf(reader.Dbref, roles);
			if (level is null)
				return new WikiDecision(action, false, false, null, set.Target) { Membership = "members only" };
			if (level < needed)
				return new WikiDecision(action, false, false, null, set.Target)
				{
					Membership = $"needs a contributor or lead, and you are a {level.Value.ToString().ToLowerInvariant()}"
				};
		}

		return new WikiDecision(action, true, false, null, null);
	}
}
