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

	/// <summary>True when no action requires anything, which is the same as having no set.</summary>
	public bool IsEmpty => Required.Values.All(scopes => scopes.Count == 0);

	/// <summary>The edit and delete requirement that <c>@wiki/protect</c> sets: wiki.admin for both.</summary>
	public static IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> Protection { get; } =
		new Dictionary<WikiAction, IReadOnlyList<string>>
		{
			[WikiAction.Edit] = [PortalPermission.WikiAdmin],
			[WikiAction.Delete] = [PortalPermission.WikiAdmin],
		};
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

	/// <summary>True when the page itself carries a set (what used to be a protected page).</summary>
	public bool HasPageRules(string pageId) => _sets.ContainsKey(WikiRuleTarget.ForPage(pageId));

	private static WikiRuleTarget Normalize(WikiRuleTarget target)
		=> target.Scope == WikiRuleScope.Page ? target : target with { Key = target.Key.ToLowerInvariant() };
}
