namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for managing text files across any number of categories.
/// Supports both PennMUSH .txt format and markdown .md files.
/// Categories are auto-discovered from subdirectories.
/// </summary>
public interface ITextFileService
{
	/// <summary>
	/// Lists all available categories (subdirectories)
	/// </summary>
	/// <returns>List of category names</returns>
	Task<IEnumerable<string>> ListCategoriesAsync();

	/// <summary>
	/// Lists all entry names/indexes in a file.
	/// For category-aware lookup, use "category/filename".
	/// For global lookup, use just "filename".
	/// </summary>
	/// <param name="fileReference">File reference: "filename" or "category/filename"</param>
	/// <param name="separator">Separator for the returned list (default: space)</param>
	/// <returns>Space or separator-delimited list of entry names</returns>
	Task<string> ListEntriesAsync(string fileReference, string separator = " ");

	/// <summary>
	/// Gets the content of a specific entry.
	/// Supports both "filename" (searches all categories) and "category/filename" (specific category).
	/// </summary>
	/// <param name="fileReference">File reference: "filename" or "category/filename"</param>
	/// <param name="entryName">Name of the entry (case-insensitive)</param>
	/// <returns>Entry content, or null if not found</returns>
	Task<string?> GetEntryAsync(string fileReference, string entryName);

	/// <summary>
	/// Lists all text files in a category
	/// </summary>
	/// <param name="category">Category name, or null for all categories</param>
	/// <returns>List of file names</returns>
	Task<IEnumerable<string>> ListFilesAsync(string? category = null);

	/// <summary>
	/// Gets the full content of a text file
	/// </summary>
	/// <param name="fileReference">File reference: "filename" or "category/filename"</param>
	/// <returns>File content, or null if not found</returns>
	Task<string?> GetFileContentAsync(string fileReference);

	/// <summary>
	/// Searches for entries matching a pattern
	/// </summary>
	/// <param name="fileReference">File reference: "filename" or "category/filename"</param>
	/// <param name="pattern">Search pattern (supports wildcards)</param>
	/// <returns>List of matching entry names</returns>
	Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern);

	/// <summary>
	/// Re-indexes all text files (rebuilds all category indexes)
	/// Called by @readcache command or on startup
	/// </summary>
	Task ReindexAsync();
}
