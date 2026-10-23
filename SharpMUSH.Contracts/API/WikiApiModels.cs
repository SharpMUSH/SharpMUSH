namespace SharpMUSH.Library.API;

/// <summary>
/// The wire contract for <c>/api/wiki/...</c>, declared once for both ends. The server's controllers
/// and the browser's <c>WikiService</c> bind the same records, so a field added, renamed or made
/// nullable on one side cannot silently stop round-tripping on the other.
/// </summary>
/// <remarks>
/// These records carry no game types on purpose: <c>SharpMUSH.Contracts</c> is the browser-safe
/// assembly, and a wire DTO that referenced <c>WikiPage</c> would drag the server's model closure
/// into the WASM bundle. Mapping from <c>WikiPage</c> lives on the server side of the boundary.
/// </remarks>
/// <param name="MarkdownSource">The page body, so the editor can round-trip an edit without a second fetch.</param>
/// <param name="IsRestricted">Some requirement applies to the page: its own (<c>@wiki/require</c>, or protection), or
/// one its namespace or a category puts on reading, editing or deleting it.</param>
public record WikiPageDto(
	string Id,
	string Slug,
	string Title,
	string Namespace,
	string MarkdownSource,
	string RenderedHtml,
	string PlainText,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	bool IsRestricted,
	int RevisionNumber,
	IReadOnlyList<string>? Categories,
	bool Published)
{
	/// <summary>
	/// What the reader may do with the page, so a client shows only the buttons that will work. Null on a
	/// payload that predates it, which a client reads as "nothing".
	/// </summary>
	public WikiAccessDto? Access { get; init; }

	/// <summary>
	/// The categories the page is in, as keys. The page holds the list; its text does not set it.
	/// Declared nullable on the constructor and normalised here so that a payload without the array
	/// binds as empty rather than handing the browser a null through a non-nullable property.
	/// </summary>
	public IReadOnlyList<string> Categories { get; init; } = Categories ?? [];

	// Localization fields are init-only with defaults so that the non-localized mapping — used by
	// every endpoint that has not been localized yet — keeps its current shape.

	/// <summary>The locale actually served.</summary>
	public string Locale { get; init; } = string.Empty;

	/// <summary>The normalised locale the reader asked for.</summary>
	public string RequestedLocale { get; init; } = string.Empty;

	/// <summary>True when a different language was served than requested — drives the reader's notice.</summary>
	public bool IsFallback { get; init; }

	/// <summary>Locales this reader can actually read the page in, source locale first.</summary>
	public IReadOnlyList<string> AvailableLocales { get; init; } = [];

	/// <summary>The name of the player who last edited the page; null when the editor is gone.</summary>
	public string? LastEditedBy { get; init; }

	/// <summary>The first image in the page's Markdown, or null. It is the page's banner only when it opens
	/// the page (<c>WikiImages.LeadImageUrl</c>).</summary>
	public string? Image { get; init; }

	/// <summary>The facts the page states that this reader may see, ordered by property and then value.</summary>
	public IReadOnlyList<WikiFactDto> Facts { get; init; } = [];

	/// <summary>For each page property, the pages this reader may see whose facts name this page.</summary>
	public IReadOnlyList<WikiFactNamedByDto> NamedBy { get; init; } = [];
}

/// <summary>
/// One fact a page states: a property and one of its values as stored. A value naming a page carries that page's
/// title, slug and namespace, so a client shows and links the page; a page the reader may not read is left out.
/// </summary>
public record WikiFactDto(string Property, string Value)
{
	/// <summary>What a reader is shown for the property: its label, or the property when it has none. Unread on a write.</summary>
	public string? Label { get; init; }

	public string? PageTitle { get; init; }
	public string? PageSlug { get; init; }
	public string? PageNamespace { get; init; }
}

/// <summary>
/// The pages whose <paramref name="Property"/> names a page, shown under <paramref name="Label"/>: up to a limit of them,
/// and whether <paramref name="More"/> do.
/// </summary>
public record WikiFactNamedByDto(string Property, string Label, IReadOnlyList<WikiPageLinkDto> Pages, bool More);

/// <summary>A page to show and link: its title, slug and namespace.</summary>
public record WikiPageLinkDto(string Title, string Slug, string Namespace);

/// <summary>
/// A property facts may state. <paramref name="Type"/> is <c>number</c>, <c>keyword</c>, <c>boolean</c> or
/// <c>page</c>; <paramref name="Keywords"/> lists a keyword property's allowed values. <paramref name="Label"/> and
/// <paramref name="Description"/> are what a reader is shown, empty when unset.
/// </summary>
public record WikiFactPropertyDto(string Id, string Type, bool Multiple, IReadOnlyList<string> Keywords,
	string Label = "", string Description = "");

/// <summary>
/// Request body for <c>PUT /api/wiki/fact-properties/{id}</c>: the property's whole definition, replacing the one it
/// had. <paramref name="Type"/> is written as <see cref="WikiFactPropertyDto.Type"/> is.
/// </summary>
public record SetFactPropertyRequest(string Type, bool Multiple, IReadOnlyList<string>? Keywords, string? Label, string? Description);

/// <summary>
/// Request body for <c>PUT /api/wiki/{slug}/facts</c>: every fact the page states, replacing the ones it had. A page
/// value may be the page's title or its id.
/// </summary>
public record SetFactsRequest(IReadOnlyList<WikiFactDto>? Facts);

/// <summary>
/// One page of <c>GET /api/wiki/query</c>'s answer. <paramref name="Next"/> continues it as <c>after</c>; null when
/// nothing is left. A page may hold fewer rows than were asked for, even none, and still go on.
/// </summary>
public record WikiFactQueryResultDto(IReadOnlyList<WikiPageSummaryDto> Pages, string? Next)
{
	/// <summary>The properties the query's <c>show=</c> names, in order: the columns beside each page.</summary>
	public IReadOnlyList<WikiFactPropertyDto> Show { get; init; } = [];

	/// <summary>By page id, the facts each page states for <see cref="Show"/>'s properties that the reader may see.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<WikiFactDto>> Facts { get; init; } = new Dictionary<string, IReadOnlyList<WikiFactDto>>();
}

/// <summary>
/// One row of a wiki listing (recent changes, a namespace, a category, the paged index): the page
/// without its body. A listing renders link rows and metadata columns, so the Markdown, HTML and plain
/// text stay on the server; the banner image, the one thing a row takes from the body, is found there.
/// </summary>
/// <param name="IsRestricted">The page carries requirements of its own.</param>
/// <param name="Locale">The locale the row's title came from.</param>
/// <param name="IsFallback">True when the title is a fallback rather than the requested language.</param>
/// <param name="Image">The first image in the page, or null.</param>
/// <param name="LastEditedBy">The name of the player who last edited the page; null when the editor is gone.</param>
public record WikiPageSummaryDto(
	string Id,
	string Slug,
	string Title,
	string Namespace,
	DateTimeOffset UpdatedAt,
	bool IsRestricted,
	int RevisionNumber,
	IReadOnlyList<string>? Categories,
	bool Published,
	string? Locale,
	bool IsFallback,
	string? Image,
	string? LastEditedBy)
{
	/// <summary>The browsing collection's stable ID, when supplied by a collection listing.</summary>
	public string? CollectionId { get; init; }
	public string? CollectionName { get; init; }

	/// <inheritdoc cref="WikiPageDto.Categories"/>
	public IReadOnlyList<string> Categories { get; init; } = Categories ?? [];

	/// <summary>The served locale; a payload without one binds as empty, as on <see cref="WikiPageDto"/>.</summary>
	public string Locale { get; init; } = Locale ?? string.Empty;
}

/// <summary>
/// <c>GET /api/wiki/counts</c>: the pages the caller may see, by state. A caller who may not see drafts is
/// told only about published pages and their own drafts. <see cref="Restricted"/> is how many pages carry
/// requirements of their own.
/// </summary>
public record WikiPageCountsDto(int Total, int Published, int Drafts, int Restricted);

/// <summary>
/// One row of <c>GET /api/wiki/categories</c>: a category any page the caller may see is filed in, that has a
/// page of its own, or that is pinned. <see cref="Name"/> is its page's title in the reader's locale, else its key
/// as a title; <see cref="Pages"/> counts the pages filed in it that the caller may see; <see cref="Pinned"/> says
/// whether it has a card on the wiki home.
/// </summary>
public record WikiCategorySummaryDto(string Key, string Name, int Pages, bool HasPage, bool Pinned = false)
{
	/// <summary>The first image of a visible preview page, when navigation supplies one.</summary>
	public string? Image { get; init; }
}

/// <summary>A translation without its body — enough for locale lists and hreflang.</summary>
public record WikiTranslationSummaryDto(
	string Locale,
	string Title,
	bool Published,
	DateTimeOffset UpdatedAt,
	int RevisionNumber);

/// <summary>A single revision snapshot. <paramref name="MarkdownSource"/> is the full page body at that revision.</summary>
public record WikiRevisionDto(
	int RevisionNumber,
	string EditorDbref,
	DateTimeOffset Timestamp,
	string? EditSummary,
	string MarkdownSource)
{
	/// <summary>The editor's name, resolved from <see cref="EditorDbref"/>; null when the player is gone.</summary>
	public string? EditorName { get; init; }
}

/// <summary>Request body for creating or updating one locale's translation of a page.</summary>
/// <param name="ExpectedRevisionNumber">
/// The <c>RevisionNumber</c> the editor loaded, for optimistic concurrency. Null means create-only.
/// A stale value is answered with 409 and must not be retried.
/// </param>
public record UpsertTranslationRequest(
	string Title,
	string Markdown,
	string? EditSummary,
	bool Published,
	int? ExpectedRevisionNumber);

/// <summary>Request body for creating a new wiki page, with the categories it starts in.</summary>
public record CreatePageRequest(string Title, string Markdown, string? Namespace, IReadOnlyList<string>? Categories = null);

/// <summary>Request body for updating an existing wiki page.</summary>
public record UpdatePageRequest(string Markdown, string? EditSummary);

/// <summary>
/// Request body for protecting a page: a page requirement of <c>wiki.admin</c> to edit and delete it
/// (true), or none (false).
/// </summary>
public record SetProtectionRequest(bool IsProtected);

/// <summary>Request body for rolling a page back to an earlier revision.</summary>
public record RollbackRequest(int RevisionNumber);

/// <summary>
/// Request body for the batch existence check. Refs use URL-path form: <c>ns/slug</c> (canonical) or
/// <c>slug</c> (main namespace).
/// </summary>
public record ExistsRequest(string[] Refs);

/// <summary>
/// Request body for setting page metadata: the categories the page is in (keyed on save; null or empty
/// leaves it in none) and whether it is published.
/// </summary>
public record SetMetadataRequest(IReadOnlyList<string>? Categories, bool Published);

/// <summary>Request body for <c>PUT /api/wiki/categories/{category}/pin</c>: whether the category has a card on the wiki home.</summary>
public record SetCategoryPinnedRequest(bool Pinned);

/// <summary>Request body for batch protection changes. Refs use <c>ns/slug</c> form.</summary>
public record BatchProtectRequest(string[] Refs, bool IsProtected);

/// <summary>Request body for batch deletion. Refs use <c>ns/slug</c> form.</summary>
public record BatchDeleteRequest(string[] Refs);

/// <summary>Per-ref outcome of a batch operation.</summary>
public record WikiBatchResult(IReadOnlyList<string> Succeeded, IReadOnlyList<string> Failed);

/// <summary>Request body for evicting pre-render cache entries after an edit.</summary>
public record InvalidateCacheRequest(string? Path, string? Prefix);

/// <summary>What the reader may do with one page. <paramref name="Manage"/> is protecting, publishing and
/// setting requirements: the <c>wiki.admin</c> permission.</summary>
public record WikiAccessDto(bool Read, bool Edit, bool Delete, bool Manage);

/// <summary>
/// What a namespace, category or page requires: <paramref name="Required"/> maps an action (<c>read</c>,
/// <c>create</c>, <c>edit</c>, <c>delete</c>) to the permissions it needs, every one of them.
/// </summary>
/// <param name="Scope"><c>namespace</c>, <c>category</c> or <c>page</c>.</param>
/// <param name="Key">The namespace name, the category key, or the page id.</param>
/// <param name="Label">What a reader calls the target: the namespace, the category, or the page title.</param>
public record WikiRequirementSetDto(
	string Scope,
	string Key,
	string Label,
	IReadOnlyDictionary<string, IReadOnlyList<string>> Required,
	string? UpdatedBy,
	DateTimeOffset? UpdatedAt);

/// <summary>
/// <c>GET /api/wiki/{slug}/requirements</c>: what the page requires itself and what its namespace and
/// categories require of it. All of them apply.
/// </summary>
public record WikiPageRequirementsDto(WikiRequirementSetDto Page, IReadOnlyList<WikiRequirementSetDto> Inherited);

/// <summary>
/// Request body for <c>PUT /api/wiki/requirements/{scope}/{key}</c>: the actions named are replaced, an
/// action with an empty list requires nothing again, and actions left out keep what they had.
/// </summary>
public record SetRequirementsRequest(IReadOnlyDictionary<string, IReadOnlyList<string>> Required);

/// <summary>One action's answer for <c>GET /api/wiki/{slug}/access</c>: whether it is allowed, and why.</summary>
public record WikiAccessExplanationDto(string Action, bool Allowed, string Reason);

/// <summary>
/// The newest few pages of one category in one namespace, and how many pages that category holds there.
/// </summary>
/// <param name="Pages">The newest pages, newest first, the excluded page left out.</param>
/// <param name="Total">Every page of the category in the namespace the caller may see, the excluded page included.</param>
public sealed record WikiCategoryNewestDto(IReadOnlyList<WikiPageSummaryDto> Pages, int Total);

/// <summary>Fact-query conditions as a person types them on one line: <c>@wiki/query</c> and the portal's search box.</summary>
public static class WikiFactConditions
{
	/// <summary>
	/// The conditions, separated by commas. A piece with no operator (<c>=</c>, <c>&lt;</c> or <c>&gt;</c>) is the rest
	/// of the value before it, so a title or category with a comma in it, as in <c>pf2e:class=Smith, John</c>, stays
	/// one condition.
	/// </summary>
	public static IReadOnlyList<string> Split(string typed)
	{
		var conditions = new List<string>();
		foreach (var piece in typed.Split(','))
		{
			if (conditions.Count > 0 && piece.IndexOfAny(['=', '<', '>']) < 0) conditions[^1] += "," + piece;
			else conditions.Add(piece);
		}
		return conditions;
	}
}
