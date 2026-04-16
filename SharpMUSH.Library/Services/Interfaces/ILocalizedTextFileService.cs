namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Locale-aware wrapper around <see cref="ITextFileService"/>.
/// When a <paramref name="locale"/> is supplied the service first looks for a
/// locale-specific category directory (<c>{category}.{locale}</c>, e.g.
/// <c>help.fr</c>) and falls back to the default category when no
/// locale-specific content is available.
///
/// File-naming convention example:
/// <code>
///   text/help/       (English baseline)
///   text/help.fr/    (French overrides — optional)
///   text/help.de/    (German overrides — optional)
/// </code>
///
/// A locale of <c>null</c>, empty, or <c>"en"</c> skips the locale lookup
/// and goes directly to the baseline.
/// </summary>
public interface ILocalizedTextFileService
{
	/// <summary>
	/// Gets the content of a specific entry, preferring the locale-specific
	/// category when available.
	/// </summary>
	Task<string?> GetEntryAsync(string fileReference, string entryName, string? locale = null);

	/// <summary>
	/// Gets the full content of a text file, preferring the locale-specific
	/// category when available.
	/// </summary>
	Task<string?> GetFileContentAsync(string fileReference, string? locale = null);

	/// <summary>
	/// Lists all entry names in a file/category, preferring the locale-specific
	/// category when available.
	/// </summary>
	Task<string> ListEntriesAsync(string fileReference, string separator = " ", string? locale = null);

	/// <summary>
	/// Searches for entries whose names match <paramref name="pattern"/>,
	/// preferring the locale-specific category when available.
	/// </summary>
	Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern, string? locale = null);

	/// <summary>
	/// Searches entry bodies for content containing <paramref name="searchTerm"/>,
	/// preferring the locale-specific category when available.
	/// </summary>
	Task<IEnumerable<string>> SearchContentAsync(string fileReference, string searchTerm, string? locale = null);
}
