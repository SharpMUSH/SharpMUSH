namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A translation without its body — enough for the editor's locale list, the reader's language chips
/// and <c>hreflang</c> generation without loading Markdown or HTML for every language.
/// </summary>
public record WikiTranslationSummary(
	string Locale,
	string Title,
	bool Published,
	DateTimeOffset UpdatedAt,
	int RevisionNumber);
