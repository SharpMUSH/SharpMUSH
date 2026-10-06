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

