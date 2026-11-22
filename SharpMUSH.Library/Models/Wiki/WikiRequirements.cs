using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models.Wiki;

/// <summary>What a reader does to a wiki page. Each has a global scope (<see cref="WikiRequirements.GlobalScope"/>).</summary>
public enum WikiAction
{
	Read,
	Create,
	Edit,
	Delete,
}

/// <summary>What a set of wiki requirements is attached to.</summary>
public enum WikiRuleScope
{
	Namespace,
	Category,
	Page,
}

/// <summary>
/// A namespace (by its lowercase name), a category (by <c>WikiHelpers.CategoryKey</c>) or a page (by its id,
/// so the rules follow the page through a rename).
/// </summary>
public readonly record struct WikiRuleTarget(WikiRuleScope Scope, string Key)
{
	public static WikiRuleTarget ForNamespace(WikiNamespace ns) => new(WikiRuleScope.Namespace, ns.ToString().ToLowerInvariant());
	public static WikiRuleTarget ForNamespace(string ns) => new(WikiRuleScope.Namespace, ns.ToLowerInvariant());
	public static WikiRuleTarget ForCategory(string categoryKey) => new(WikiRuleScope.Category, categoryKey);
	public static WikiRuleTarget ForPage(string pageId) => new(WikiRuleScope.Page, pageId);

	/// <summary>"namespace character", "category lore" or "page 12".</summary>
	public override string ToString() => $"{Scope.ToString().ToLowerInvariant()} {Key}";
}

/// <summary>What a write of a <see cref="WikiRequirementSet"/> keeps from the stored row instead of replacing it.</summary>
public enum WikiRequirementKeep
{
	/// <summary>The set replaces the stored row.</summary>
	Nothing,

	/// <summary>The set's permissions are written; its members are the stored row's.</summary>
	Members,

	/// <summary>The set's members are written; its permissions are the stored row's.</summary>
	Required,
}

/// <summary>
/// The permissions a namespace, category or page requires for each action, on top of the action's global
/// scope. Every listed permission is required. An action left out requires nothing extra.
/// </summary>
public sealed record WikiRequirementSet(
	WikiRuleTarget Target,
	IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> Required,
	string UpdatedBy,
	DateTimeOffset UpdatedAt)
{
	/// <summary>The permissions <paramref name="action"/> requires here; empty for none.</summary>
	public IReadOnlyList<string> For(WikiAction action) => Required.TryGetValue(action, out var scopes) ? scopes : [];

	/// <summary>
	/// Who may use what this set is on, when it is private: the characters and roles let in, each at a
	/// <see cref="WikiMemberLevel"/>. Empty for an area anyone holding the permissions may use.
	/// </summary>
	public IReadOnlyList<WikiMember> Members { get; init; } = [];

	/// <summary>True when the set has members, so only they (and <c>wiki.admin</c>) may read it.</summary>
	public bool IsPrivate => Members.Count > 0;

	/// <summary>
	/// What a write of this set leaves as it is stored when the write commits, so a change to what an area
	/// requires and a change to who is in it never undo each other.
	/// </summary>
	public WikiRequirementKeep Keep { get; init; }

	/// <summary>
	/// The highest level <paramref name="dbref"/> holds here, directly or through one of <paramref name="roles"/>;
	/// null for someone who is not a member.
	/// </summary>
	public WikiMemberLevel? LevelOf(string? dbref, IReadOnlySet<string> roles)
	{
		WikiMemberLevel? best = null;
		foreach (var member in Members)
		{
			var held = member.RoleSlug is { } slug
				? roles.Contains(slug)
				: dbref is { Length: > 0 } me && member.NamesCharacter(me);
			if (held && (best is null || member.Level > best)) best = member.Level;
		}

		return best;
	}

	/// <summary>True when no action requires anything and nobody is a member, which is the same as having no set.</summary>
	public bool IsEmpty => Required.Values.All(scopes => scopes.Count == 0) && Members.Count == 0;

	/// <summary>The edit and delete requirement that <c>@wiki/protect</c> sets: wiki.admin for both.</summary>
	public static IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> Protection { get; } =
		new Dictionary<WikiAction, IReadOnlyList<string>>
		{
			[WikiAction.Edit] = [PortalPermission.WikiAdmin],
			[WikiAction.Delete] = [PortalPermission.WikiAdmin],
		};
}

/// <summary>
/// How far a member of a private wiki area may go. Each level includes the ones before it: a reader reads,
/// a contributor also writes pages and posts developments, a lead also lets people in and out.
/// </summary>
public enum WikiMemberLevel
{
	Reader,
	Contributor,
	Lead,
}

/// <summary>
/// One entry of a private area's member list: a character by its objid (<c>#12:1700000000000</c>), or every
/// holder of a role, written <c>role:&lt;slug&gt;</c>.
/// </summary>
public sealed record WikiMember(string Principal, WikiMemberLevel Level)
{
	/// <summary>The prefix of a role principal.</summary>
	public const string RolePrefix = "role:";

	/// <summary>A role principal for <paramref name="slug"/>.</summary>
	public static string ForRole(string slug) => RolePrefix + slug.Trim().ToLowerInvariant();

	/// <summary>The role's slug when this entry names a role; null for a character.</summary>
	public string? RoleSlug => Principal.StartsWith(RolePrefix, StringComparison.OrdinalIgnoreCase)
		? Principal[RolePrefix.Length..]
		: null;

	/// <summary>The lowest level an action needs: reading needs <see cref="WikiMemberLevel.Reader"/>, every write <see cref="WikiMemberLevel.Contributor"/>.</summary>
	public static WikiMemberLevel Needed(WikiAction action)
		=> action == WikiAction.Read ? WikiMemberLevel.Reader : WikiMemberLevel.Contributor;

	/// <summary>
	/// True when a character principal names <paramref name="dbref"/>: the same number, and the same creation
	/// time whenever both carry one, so a recycled dbref is never a member by accident.
	/// </summary>
	public bool NamesCharacter(string dbref)
	{
		if (RoleSlug is not null) return false;
		var (number, creation) = Split(Principal);
		var (other, otherCreation) = Split(dbref);
		return number is not null && number == other
			&& (creation is null || otherCreation is null || creation == otherCreation);
	}

	private static (string? Number, string? Creation) Split(string dbref)
	{
		var text = dbref.Trim().TrimStart('#');
		var colon = text.IndexOf(':');
		return colon < 0
			? (text.Length > 0 ? text : null, null)
			: (text[..colon], text[(colon + 1)..]);
	}
}

/// <summary>One permission a page requires for an action, and the namespace, category or page that requires it.</summary>
public readonly record struct WikiRequirement(WikiRuleTarget Source, WikiAction Action, string Scope);

/// <summary>
/// Every requirement set in the world, and the rule that combines them: a page requires everything its
/// namespace, each of its categories and the page itself require, for the action and for reading it, and
/// deleting it also requires what editing it does. Requirements only add; no level lifts another's.
/// </summary>
public sealed class WikiRequirements
{
	private readonly Dictionary<WikiRuleTarget, WikiRequirementSet> _sets;

	public WikiRequirements(IEnumerable<WikiRequirementSet> sets)
		=> _sets = sets.Where(set => !set.IsEmpty).ToDictionary(set => Normalize(set.Target));

	public static WikiRequirements Empty { get; } = new([]);

	/// <summary>Every non-empty set, namespaces first, then categories, then pages, each by key.</summary>
	public IReadOnlyList<WikiRequirementSet> Sets
		=> _sets.Values.OrderBy(set => set.Target.Scope).ThenBy(set => set.Target.Key, StringComparer.Ordinal).ToList();

	/// <summary>The set on <paramref name="target"/>, or null for none.</summary>
	public WikiRequirementSet? For(WikiRuleTarget target) => _sets.GetValueOrDefault(Normalize(target));

	/// <summary>The targets whose sets name <paramref name="scope"/>.</summary>
	public IReadOnlyList<WikiRuleTarget> Naming(string scope)
		=> Sets.Where(set => set.Required.Values.Any(scopes => scopes.Contains(scope, StringComparer.OrdinalIgnoreCase)))
			.Select(set => set.Target).ToList();

	/// <summary>The global scope every reader needs for <paramref name="action"/>, whatever the page.</summary>
	public static string GlobalScope(WikiAction action) => action switch
	{
		WikiAction.Read => PortalPermission.WikiRead,
		WikiAction.Create => PortalPermission.WikiCreate,
		WikiAction.Edit => PortalPermission.WikiEdit,
		WikiAction.Delete => PortalPermission.WikiDelete,
		_ => throw new ArgumentOutOfRangeException(nameof(action)),
	};

	/// <summary>
	/// What <paramref name="action"/> requires of a page in <paramref name="ns"/> carrying
	/// <paramref name="categories"/>, with <paramref name="pageId"/> null for a page not created yet.
	/// </summary>
	public IReadOnlyList<WikiRequirement> Required(string ns, IEnumerable<string> categories, string? pageId, WikiAction action)
	{
		var targets = new List<WikiRuleTarget> { WikiRuleTarget.ForNamespace(ns) };
		targets.AddRange(categories.Select(WikiRuleTarget.ForCategory));
		if (pageId is not null) targets.Add(WikiRuleTarget.ForPage(pageId));

		var actions = action switch
		{
			WikiAction.Read => [WikiAction.Read],
			WikiAction.Delete => [WikiAction.Read, WikiAction.Edit, WikiAction.Delete],
			_ => new[] { WikiAction.Read, action },
		};

		return targets
			.Select(For)
			.OfType<WikiRequirementSet>()
			.SelectMany(set => actions.SelectMany(a => set.For(a).Select(scope => new WikiRequirement(set.Target, a, scope))))
			.ToList();
	}

	/// <inheritdoc cref="Required(string, IEnumerable{string}, string?, WikiAction)"/>
	public IReadOnlyList<WikiRequirement> Required(WikiPage page, WikiAction action)
		=> Required(page.Namespace, page.Categories, page.Id, action);

	/// <summary>The private sets a page sits in: its namespace's, each of its categories' and its own, such as a private page's.</summary>
	public IReadOnlyList<WikiRequirementSet> PrivateSets(string ns, IEnumerable<string> categories, string? pageId)
	{
		var targets = new List<WikiRuleTarget> { WikiRuleTarget.ForNamespace(ns) };
		targets.AddRange(categories.Select(WikiRuleTarget.ForCategory));
		if (pageId is not null) targets.Add(WikiRuleTarget.ForPage(pageId));
		return targets.Distinct().Select(For).OfType<WikiRequirementSet>().Where(set => set.IsPrivate).ToList();
	}

	/// <summary>Every private set: the areas with members.</summary>
	public IReadOnlyList<WikiRequirementSet> Private => Sets.Where(set => set.IsPrivate).ToList();

	/// <summary>True when the page itself carries a set (what used to be a protected page).</summary>
	public bool HasPageRules(string pageId) => _sets.ContainsKey(WikiRuleTarget.ForPage(pageId));

	private static WikiRuleTarget Normalize(WikiRuleTarget target)
		=> target.Scope == WikiRuleScope.Page ? target : target with { Key = target.Key.ToLowerInvariant() };
}
