using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Who is reading the wiki: the permissions they hold, resolved by the role system, and the dbref their
/// own drafts are stored under (null for an anonymous reader).
/// </summary>
public sealed record WikiReader(IReadOnlySet<string> Scopes, string? Dbref)
{
	/// <summary>A reader holding <paramref name="scopes"/>, compared without case.</summary>
	public static WikiReader From(IEnumerable<string> scopes, string? dbref)
		=> new(scopes.ToHashSet(StringComparer.OrdinalIgnoreCase), dbref);

	public bool Has(string scope) => Scopes.Contains(scope);

	/// <summary>A <c>wiki.admin</c> holder skips namespace, category and page requirements.</summary>
	public bool Bypasses => Has(Authorization.PortalPermission.WikiAdmin);
}

/// <summary>
/// Whether a reader may take one action on one page, and why. <paramref name="MissingScope"/> is the
/// permission that refused it; <paramref name="RequiredBy"/> is the namespace, category or page that
/// required it, null when the global scope for the action was missing.
/// </summary>
public sealed record WikiDecision(WikiAction Action, bool Allowed, bool Bypassed, string? MissingScope, WikiRuleTarget? RequiredBy)
{
	/// <summary>One line: "allowed", "allowed (wiki.admin skips requirements)", "needs wiki.edit", "category lore requires lore.edit" or "the page requires wiki.admin".</summary>
	public string Describe() => (Allowed, Bypassed, RequiredBy) switch
	{
		(true, true, _) => "allowed (wiki.admin skips requirements)",
		(true, false, _) => "allowed",
		(false, _, null) => $"needs {MissingScope}",
		(false, _, { Scope: WikiRuleScope.Page }) => $"the page requires {MissingScope}",
		(false, _, { } source) => $"{source} requires {MissingScope}",
	};
}

/// <summary>What a reader may do with one page, for a client deciding which buttons to show.</summary>
/// <param name="Manage">Protect, publish and set requirements: the global <c>wiki.admin</c> scope.</param>
public readonly record struct WikiAccess(bool Read, bool Edit, bool Delete, bool Manage);

/// <summary>
/// The one place wiki permissions are decided, for the portal, <c>@wiki</c> and the wiki functions alike.
/// An action needs its global scope (<c>wiki.read</c>, <c>wiki.create</c>, <c>wiki.edit</c>,
/// <c>wiki.delete</c>) and every permission the page's namespace, categories and the page itself require
/// (<see cref="WikiRequirements"/>). A <c>wiki.admin</c> holder skips the requirements, not the global scope.
/// Drafts other than one's own need <c>wiki.drafts</c>.
/// </summary>
public interface IWikiAccessService
{
	/// <summary>A reader who is not logged in: what the <c>everyone</c> role grants.</summary>
	ValueTask<WikiReader> AnonymousAsync(CancellationToken ct = default);

	/// <summary>A game object as a reader: what it is granted, and its dbref as <c>@wiki</c> stores authors.</summary>
	ValueTask<WikiReader> ForObjectAsync(AnySharpObject obj, CancellationToken ct = default);

	/// <summary>Every requirement set, cached until one changes.</summary>
	Task<WikiRequirements> RequirementsAsync();

	/// <summary>
	/// Whether <paramref name="reader"/> may take <paramref name="action"/> on <paramref name="page"/>, by the
	/// global scopes and the requirements. The draft rule is apart (<see cref="MaySeeDraft"/>).
	/// </summary>
	Task<WikiDecision> DecideAsync(WikiReader reader, WikiPage page, WikiAction action);

	/// <summary>The draft rule: a published page, or the reader wrote it, or the reader holds <c>wiki.drafts</c>.</summary>
	bool MaySeeDraft(WikiReader reader, WikiPage page);

	/// <summary>Whether <paramref name="reader"/> may create a page in <paramref name="ns"/> filed in <paramref name="categories"/>.</summary>
	Task<WikiDecision> DecideCreateAsync(WikiReader reader, string ns, IEnumerable<string> categories);

	/// <summary>
	/// Whether <paramref name="reader"/> may file <paramref name="page"/> in <paramref name="categories"/>: an edit
	/// that must be allowed under the page's current categories and under the new ones.
	/// </summary>
	Task<WikiDecision> DecideCategoriesAsync(WikiReader reader, WikiPage page, IEnumerable<string> categories);

	/// <summary>Everything <paramref name="reader"/> may do with <paramref name="page"/>.</summary>
	Task<WikiAccess> ForPageAsync(WikiReader reader, WikiPage page);

	/// <summary>The pages a listing shows <paramref name="reader"/>, for a store to apply before paging.</summary>
	Task<WikiVisibility> VisibilityAsync(WikiReader reader);

	/// <summary>Whether <paramref name="reader"/> may see <paramref name="page"/>: it may read it and the draft rule admits it.</summary>
	Task<bool> CanSeeAsync(WikiReader reader, WikiPage page);

	/// <summary>
	/// Sets what <paramref name="target"/> requires for the actions in <paramref name="changes"/>, keeping the
	/// rest; an action mapped to no permissions requires nothing again. Needs <c>wiki.admin</c>, and every
	/// permission named must be a built-in or a defined custom one. Returns every requirement set afterwards;
	/// <c>NotFound</c> for a page target naming no page.
	/// </summary>
	Task<FoundResult<WikiRequirements>> SetRequirementsAsync(WikiReader actor, string actorDbref, WikiRuleTarget target,
		IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> changes);
}
