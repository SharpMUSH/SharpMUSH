using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Locale-aware text file service that wraps <see cref="ITextFileService"/>.
/// For each request it first tries the locale-specific category directory
/// (<c>{category}.{locale}</c>) and falls back to the neutral baseline when
/// no locale-specific content is found.
/// </summary>
public class LocalizedTextFileService(ITextFileService textFileService) : ILocalizedTextFileService
{
	// -----------------------------------------------------------------------
	// Helpers
	// -----------------------------------------------------------------------

	/// <summary>
	/// Returns true when the locale requires a locale-aware lookup (i.e. it is
	/// non-null, non-empty, and not the English baseline).
	/// </summary>
	private static bool NeedsLocale(string? locale)
		=> !string.IsNullOrWhiteSpace(locale)
		   && !locale.Equals("en", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Builds the locale-specific file reference.
	/// <list type="bullet">
	///   <item><c>"help" + "fr"    → "help.fr"</c></item>
	///   <item><c>"news/help" + "fr" → "news.fr/help"</c></item>
	/// </list>
	/// The locale tag is appended to the <em>category</em> portion of the path
	/// so the implementation delegates resolution to the underlying
	/// <see cref="ITextFileService"/> without any directory-probing changes.
	/// </summary>
	private static string LocalizedRef(string fileReference, string locale)
	{
		var slash = fileReference.IndexOf('/');
		return slash < 0
			? $"{fileReference}.{locale}"                          // "help"      → "help.fr"
			: $"{fileReference[..slash]}.{locale}{fileReference[slash..]}"; // "news/help" → "news.fr/help"
	}

	/// <summary>
	/// Generic locale-fallback helper. Tries the locale-specific lookup first,
	/// then falls back to the neutral baseline if the result is considered empty.
	/// </summary>
	private async Task<T> WithLocaleFallbackAsync<T>(
		string fileReference,
		string? locale,
		Func<string, Task<T>> lookup,
		Func<T, bool> isEmpty)
	{
		if (NeedsLocale(locale))
		{
			var result = await lookup(LocalizedRef(fileReference, locale!));
			if (!isEmpty(result))
				return result;
		}
		return await lookup(fileReference);
	}

	// -----------------------------------------------------------------------
	// ILocalizedTextFileService
	// -----------------------------------------------------------------------

	/// <inheritdoc />
	public Task<string?> GetEntryAsync(string fileReference, string entryName, string? locale = null)
		=> WithLocaleFallbackAsync(
			fileReference, locale,
			r => textFileService.GetEntryAsync(r, entryName),
			result => result is null);

	/// <inheritdoc />
	public Task<string?> GetFileContentAsync(string fileReference, string? locale = null)
		=> WithLocaleFallbackAsync(
			fileReference, locale,
			r => textFileService.GetFileContentAsync(r),
			result => result is null);

	/// <inheritdoc />
	public Task<string> ListEntriesAsync(string fileReference, string separator = " ", string? locale = null)
		=> WithLocaleFallbackAsync(
			fileReference, locale,
			r => textFileService.ListEntriesAsync(r, separator),
			string.IsNullOrEmpty);

	/// <inheritdoc />
	public Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern, string? locale = null)
		=> WithLocaleFallbackAsync(
			fileReference, locale,
			r => textFileService.SearchEntriesAsync(r, pattern),
			result => !result.Any());

	/// <inheritdoc />
	public Task<IEnumerable<string>> SearchContentAsync(string fileReference, string searchTerm, string? locale = null)
		=> WithLocaleFallbackAsync(
			fileReference, locale,
			r => textFileService.SearchContentAsync(r, searchTerm),
			result => !result.Any());
}
