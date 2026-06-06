namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A wiki page stored in a dedicated collection, separate from game objects.
/// Wiki pages are NOT SharpObjects — they have their own schema and live
/// in their own collection.
/// </summary>
/// <param name="Id">Storage key (ArangoDB _key). Empty string for unsaved pages.</param>
/// <param name="Slug">URL-friendly identifier, unique per namespace. Format: [a-z0-9_/]+</param>
/// <param name="Title">Human-readable display title.</param>
/// <param name="Namespace">Top-level namespace grouping (main, help, character, system).</param>
/// <param name="MarkdownSource">Raw Markdown content — the single source of truth.</param>
/// <param name="RenderedHtml">Cached HTML render from Markdig.</param>
/// <param name="PlainText">Plain text extracted from Markdown, used for search indexing.</param>
/// <param name="AuthorDbref">DBRef string of the player who created this page.</param>
/// <param name="LastEditorDbref">DBRef string of the player who last edited this page.</param>
/// <param name="CreatedAt">UTC timestamp of page creation.</param>
/// <param name="UpdatedAt">UTC timestamp of the last edit.</param>
/// <param name="IsProtected">When true, only Royalty+ (admin) users may edit this page.</param>
/// <param name="RevisionNumber">Monotonically increasing revision counter, starting at 1.</param>
public record WikiPage(
	string Id,
	string Slug,
	string Title,
	string Namespace,
	string MarkdownSource,
	string RenderedHtml,
	string PlainText,
	string AuthorDbref,
	string LastEditorDbref,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	bool IsProtected,
	int RevisionNumber);
