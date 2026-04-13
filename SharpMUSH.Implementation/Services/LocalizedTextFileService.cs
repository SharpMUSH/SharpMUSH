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

	// -----------------------------------------------------------------------
	// ILocalizedTextFileService
	// -----------------------------------------------------------------------

	/// <inheritdoc />
	public async Task<string?> GetEntryAsync(string fileReference, string entryName, string? locale = null)
	{
		if (NeedsLocale(locale))
		{
			var result = await textFileService.GetEntryAsync(LocalizedRef(fileReference, locale!), entryName);
			if (result is not null)
				return result;
		}
		return await textFileService.GetEntryAsync(fileReference, entryName);
	}

	/// <inheritdoc />
	public async Task<string?> GetFileContentAsync(string fileReference, string? locale = null)
	{
		if (NeedsLocale(locale))
		{
			var result = await textFileService.GetFileContentAsync(LocalizedRef(fileReference, locale!));
			if (result is not null)
				return result;
		}
		return await textFileService.GetFileContentAsync(fileReference);
	}

	/// <inheritdoc />
	public async Task<string> ListEntriesAsync(string fileReference, string separator = " ", string? locale = null)
	{
		if (NeedsLocale(locale))
		{
			var result = await textFileService.ListEntriesAsync(LocalizedRef(fileReference, locale!), separator);
			if (!string.IsNullOrEmpty(result))
				return result;
		}
		return await textFileService.ListEntriesAsync(fileReference, separator);
	}

	/// <inheritdoc />
	public async Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern, string? locale = null)
	{
		if (NeedsLocale(locale))
		{
			var result = await textFileService.SearchEntriesAsync(LocalizedRef(fileReference, locale!), pattern);
			if (result.Any())
				return result;
		}
		return await textFileService.SearchEntriesAsync(fileReference, pattern);
	}

	/// <inheritdoc />
	public async Task<IEnumerable<string>> SearchContentAsync(string fileReference, string searchTerm, string? locale = null)
	{
		if (NeedsLocale(locale))
		{
			var result = await textFileService.SearchContentAsync(LocalizedRef(fileReference, locale!), searchTerm);
			if (result.Any())
				return result;
		}
		return await textFileService.SearchContentAsync(fileReference, searchTerm);
	}
}
