namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A translation of a <see cref="WikiPage"/> into one locale — an overlay row hanging off the page,
/// not a page in its own right.
/// </summary>
/// <remarks>
/// Note what this record deliberately lacks: no <c>Category</c>, no <c>Tags</c>, no <c>IsProtected</c>,
/// no <c>Slug</c>. That absence <em>is</em> the enforcement of "a translation inherits the source
/// page's metadata" — there is nowhere for a translation to store a conflicting category, so no
/// runtime check is needed to keep the two in step.
/// </remarks>
/// <param name="Id">Storage key.</param>
/// <param name="PageId">FK to the parent <see cref="WikiPage.Id"/>.</param>
/// <param name="Locale">Canonical BCP-47 tag. Unique per <paramref name="PageId"/>.</param>
/// <param name="Title">Translated display title.</param>
/// <param name="MarkdownSource">Translated Markdown body — the source of truth for this locale.</param>
/// <param name="RenderedHtml">Cached HTML render of <paramref name="MarkdownSource"/>.</param>
/// <param name="PlainText">Plain text extracted from <paramref name="MarkdownSource"/>.</param>
/// <param name="LastEditorDbref">DBRef string of the player who last edited this translation.</param>
/// <param name="CreatedAt">UTC timestamp the translation was first written.</param>
/// <param name="UpdatedAt">UTC timestamp of the last edit to this translation.</param>
/// <param name="Published">When false this translation is a draft: invisible to ordinary readers,
/// who fall back exactly as if it did not exist.</param>
/// <param name="RevisionNumber">Per-locale revision counter, starting at 1.</param>
public record WikiTranslation(
	string Id,
	string PageId,
	string Locale,
	string Title,
	string MarkdownSource,
	string RenderedHtml,
	string PlainText,
	string LastEditorDbref,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	bool Published,
	int RevisionNumber);
