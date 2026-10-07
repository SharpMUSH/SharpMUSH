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
	IAccountStore accounts) : IWikiAccessService
{
	public async ValueTask<WikiReader> AnonymousAsync(CancellationToken ct = default)
	{
		var everyone = (await roles.GetRolesAsync(ct)).Where(BuiltInRoles.IsEveryone).ToArray();
		var custom = await roles.GetCustomPermissionsAsync(ct);
		var context = new PermissionContext(everyone, new Dictionary<string, PermissionState>(), false)
		{
			CustomScopes = custom.Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase)
		};
		return WikiReader.From(resolver.Resolve(context), null);
	}

	public async ValueTask<WikiReader> ForObjectAsync(AnySharpObject obj, CancellationToken ct = default)
	{
		var account = obj is SharpPlayer
			? await accounts.GetAccountForCharacterAsync(obj.Object().DBRef, ct)
			: null;
		var biographies = account?.Id is { Length: > 0 } accountId ? await BiographiesAsync(accountId, ct) : [];
		return WikiReader.From((await obj.GrantsAsync(ct)).Granted, $"#{obj.Object().Key}", biographies);
	}

	public async ValueTask<IReadOnlyList<string>> BiographiesAsync(string accountId, CancellationToken ct = default)
		=> [.. (await accounts.GetCharactersForAccountAsync(accountId, ct)).Select(c => WikiHelpers.Slugify(c.Object.Name))];

	public Task<WikiRequirements> RequirementsAsync() => wiki.GetRequirementsAsync();

	public async Task<WikiDecision> DecideAsync(WikiReader reader, WikiPage page, WikiAction action)
		=> OwnerDecision(reader, page.Namespace, page.Slug, action)
			?? Decide(reader, await RequirementsAsync(), page.Namespace, page.Categories, page.Id, action);

	public bool MaySeeDraft(WikiReader reader, WikiPage page)
		=> page.Published || reader.Has(PortalPermission.WikiDrafts) || IsAuthor(reader, page);

	public async Task<WikiDecision> DecideCreateAsync(WikiReader reader, string ns, IEnumerable<string> categories, string? slug = null)
		=> OwnerDecision(reader, ns, slug, WikiAction.Create)
			?? Decide(reader, await RequirementsAsync(), ns.ToLowerInvariant(),
				WikiHelpers.NormalizeCategories(categories, WikiHelpers.ParseNamespace(ns)), null,
				WikiAction.Create);

	public async Task<WikiDecision> DecideCategoriesAsync(WikiReader reader, WikiPage page, IEnumerable<string> categories)
	{
		if (OwnerDecision(reader, page.Namespace, page.Slug, WikiAction.Edit) is { } owned) return owned;
		var requirements = await RequirementsAsync();
		var before = Decide(reader, requirements, page.Namespace, page.Categories, page.Id, WikiAction.Edit);
		return before.Allowed
			? Decide(reader, requirements, page.Namespace,
				WikiHelpers.NormalizeCategories(categories, WikiHelpers.ParseNamespace(page.Namespace)), page.Id, WikiAction.Edit)
			: before;
	}

	public async Task<WikiAccess> ForPageAsync(WikiReader reader, WikiPage page)
	{
		var requirements = await RequirementsAsync();
		var read = await CanSeeAsync(reader, page);
		return new WikiAccess(
			read,
			read && (OwnerDecision(reader, page.Namespace, page.Slug, WikiAction.Edit)
				?? Decide(reader, requirements, page.Namespace, page.Categories, page.Id, WikiAction.Edit)).Allowed,
			read && Decide(reader, requirements, page.Namespace, page.Categories, page.Id, WikiAction.Delete).Allowed,
			reader.Has(PortalPermission.WikiAdmin));
	}

	public async Task<bool> CanSeeAsync(WikiReader reader, WikiPage page)
		=> MaySeeDraft(reader, page) && (await DecideAsync(reader, page, WikiAction.Read)).Allowed;

	public async Task<WikiVisibility> VisibilityAsync(WikiReader reader)
	{
		var visibility = new WikiVisibility(reader.Has(PortalPermission.WikiDrafts), reader.Dbref);
		if (!reader.Has(PortalPermission.WikiRead)) return visibility with { Hidden = WikiReadRestrictions.All };
		if (reader.Bypasses) return visibility;

		var hidden = (await RequirementsAsync()).Sets
			.Where(set => set.For(WikiAction.Read).Any(scope => !reader.Has(scope)))
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
	/// Reading, writing and editing one's own character's biography: allowed by the account owning the
	/// character, whichever of its characters is acting, with no role or permission involved. Null for any
	/// other page or action, which the permission rule decides.
	/// </summary>
	private static WikiDecision? OwnerDecision(WikiReader reader, string ns, string? slug, WikiAction action)
		=> action is WikiAction.Read or WikiAction.Create or WikiAction.Edit && reader.OwnsBiography(ns, slug)
			? new WikiDecision(action, true, false, null, null, Owner: true)
			: null;

	private static bool IsAuthor(WikiReader reader, WikiPage page)
		=> reader.Dbref is { Length: > 0 } me && string.Equals(page.AuthorDbref, me, StringComparison.Ordinal);

	/// <summary>The global scope for the action (and for reading), then, unless the reader bypasses them, every requirement.</summary>
	private static WikiDecision Decide(WikiReader reader, WikiRequirements requirements, string ns,
		IEnumerable<string> categories, string? pageId, WikiAction action)
	{
		foreach (var global in new[] { WikiRequirements.GlobalScope(WikiAction.Read), WikiRequirements.GlobalScope(action) }.Distinct())
		{
			if (!reader.Has(global)) return new WikiDecision(action, false, false, global, null);
		}

		if (reader.Bypasses) return new WikiDecision(action, true, true, null, null);

		foreach (var requirement in requirements.Required(ns, categories, pageId, action))
		{
			if (!reader.Has(requirement.Scope))
				return new WikiDecision(action, false, false, requirement.Scope, requirement.Source);
		}

		return new WikiDecision(action, true, false, null, null);
	}
}
