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
	bool IsProtected,
	int RevisionNumber,
	string? Category,
	IReadOnlyList<string>? Tags,
	bool Published)
{
	/// <summary>
	/// Tags on the page. Declared nullable on the constructor and normalised here so that a payload
	/// without the array binds as empty rather than handing the browser a null through a
	/// non-nullable property.
	/// </summary>
	public IReadOnlyList<string> Tags { get; init; } = Tags ?? [];

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
	string MarkdownSource);

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

/// <summary>Request body for creating a new wiki page. Category is part of identity and is fixed at create.</summary>
public record CreatePageRequest(string Title, string Markdown, string? Namespace, string? Category);

/// <summary>Request body for updating an existing wiki page.</summary>
public record UpdatePageRequest(string Markdown, string? EditSummary);

/// <summary>Request body for setting page protection.</summary>
public record SetProtectionRequest(bool IsProtected);

/// <summary>Request body for rolling a page back to an earlier revision.</summary>
public record RollbackRequest(int RevisionNumber);

/// <summary>
/// Request body for the batch existence check. Refs use URL-path form: <c>ns/category/slug</c>
/// (canonical), <c>ns/slug</c> (general category), or <c>slug</c> (main/general).
/// </summary>
public record ExistsRequest(string[] Refs);

/// <summary>Request body for setting page metadata.</summary>
public record SetMetadataRequest(string? Category, string[] Tags, bool Published);

/// <summary>Request body for batch protection changes. Refs use <c>ns/category/slug</c> form.</summary>
public record BatchProtectRequest(string[] Refs, bool IsProtected);

/// <summary>Request body for batch deletion. Refs use <c>ns/category/slug</c> form.</summary>
public record BatchDeleteRequest(string[] Refs);

/// <summary>Per-ref outcome of a batch operation.</summary>
public record WikiBatchResult(IReadOnlyList<string> Succeeded, IReadOnlyList<string> Failed);

/// <summary>Request body for evicting pre-render cache entries after an edit.</summary>
public record InvalidateCacheRequest(string? Path, string? Prefix);
