namespace SharpMUSH.Client.Models;

/// <summary>
/// One locale's translation of a wiki page, without its body — enough for the language chip row and the
/// editor's locale dropdown.
/// </summary>
public record WikiTranslationInfo(
	string Locale,
	string Title,
	bool Published,
	DateTimeOffset UpdatedAt,
	int RevisionNumber);
