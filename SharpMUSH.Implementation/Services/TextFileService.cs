using DotNext.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Index entry with file position metadata for efficient reads
/// </summary>
public record IndexEntry(
	string FilePath,
	long StartPosition,
	long EndPosition,
	string EntryName,
	HelpEntry? Help = null,
	bool Hidden = false
);

public class TextFileService : ITextFileService
{
	private readonly IOptions<SharpMUSHOptions> _options;
	private readonly ILogger<TextFileService> _logger;

	private readonly Dictionary<string, Dictionary<string, IndexEntry>> _categoryIndexes = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _indexLock = new();
	private readonly AsyncLazy<bool> _initializationTask;

	public TextFileService(
		IOptions<SharpMUSHOptions> options,
		ILogger<TextFileService> logger)
	{
		_options = options;
		_logger = logger;

		if (_options.Value.TextFile.CacheOnStartup)
		{
			_initializationTask = new AsyncLazy<bool>(async cancellationToken =>
			{
				await ReindexAsync();
				return true;
			});
		}
		else
		{
			_initializationTask = new AsyncLazy<bool>(cancellationToken => Task.FromResult(true));
		}
	}

	public Task<IEnumerable<string>> ListCategoriesAsync()
	{
		var baseDir = _options.Value.TextFile.TextFilesDirectory;
		if (!Directory.Exists(baseDir))
		{
			_logger.LogWarning("Text files directory does not exist: {Directory}", baseDir);
			return Task.FromResult(Enumerable.Empty<string>());
		}

		var categories = Directory.GetDirectories(baseDir)
			.Select(d => Path.GetFileName(d)!);
		return Task.FromResult(categories);
	}

	public async Task<string> ListEntriesAsync(string fileReference, string separator = " ")
	{
		await _initializationTask.WithCancellation(CancellationToken.None);

		var category = ResolveEntryCategory(fileReference);

		lock (_indexLock)
		{
			if (category != null && !_categoryIndexes.ContainsKey(category))
			{
				return string.Empty;
			}
			if (category != null && _categoryIndexes.TryGetValue(category, out var entries))
			{
				return string.Join(separator, entries.Values.Where(entry => !entry.Hidden).Select(entry => entry.EntryName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k));
			}

			var allEntries = _categoryIndexes.Values
				.SelectMany(dict => dict.Values.Where(entry => !entry.Hidden).Select(entry => entry.EntryName))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(k => k);

			return string.Join(separator, allEntries);
		}
	}

	public async Task<string?> GetEntryAsync(string fileReference, string entryName) =>
		(await GetHelpEntryAsync(fileReference, entryName))?.Markdown;

	public async Task<HelpEntry?> GetHelpEntryAsync(string fileReference, string entryName)
	{
		await _initializationTask.WithCancellation(CancellationToken.None);

		var category = ResolveEntryCategory(fileReference);

		IndexEntry? indexEntry = null;
		lock (_indexLock)
		{
			if (category != null)
			{
				if (_categoryIndexes.TryGetValue(category, out var categoryEntries))
				{
					categoryEntries.TryGetValue(entryName, out indexEntry);
				}
			}
			else
			{
				foreach (var categoryDict in _categoryIndexes.Values)
				{
					if (categoryDict.TryGetValue(entryName, out indexEntry))
					{
						break;
					}
				}
			}
		}

		if (indexEntry == null)
		{
			return null;
		}

		return indexEntry.Help;
	}

	public Task<IEnumerable<string>> ListFilesAsync(string? category = null)
	{
		var baseDir = _options.Value.TextFile.TextFilesDirectory;

		if (category != null)
		{
			var categoryPath = Path.Combine(baseDir, category);
			if (!Directory.Exists(categoryPath))
			{
				return Task.FromResult(Enumerable.Empty<string>());
			}

			var files = Directory.GetFiles(categoryPath, "*.*")
				.Select(f => Path.GetFileName(f)!);
			return Task.FromResult(files);
		}
		else
		{
			var files = Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories)
				.Select(f => Path.GetFileName(f)!)
				.Distinct();
			return Task.FromResult(files);
		}
	}

	public async Task<HelpEntry?> GetPrefixEntryAsync(string fileReference, string prefix)
	{
		await _initializationTask.WithCancellation(CancellationToken.None);
		var category = ResolveEntryCategory(fileReference);
		var regex = SoftcodeRegex.Wildcard(prefix + "*");
		lock (_indexLock)
		{
			IEnumerable<KeyValuePair<string, IndexEntry>> entries = category is null
				? _categoryIndexes.Values.SelectMany(index => index)
				: _categoryIndexes.TryGetValue(category, out var index) ? index : [];
			return entries.Where(pair => !pair.Value.Hidden && SoftcodeRegex.IsMatch(regex, pair.Key))
				.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
				.Select(pair => pair.Value.Help).FirstOrDefault();
		}
	}

	public async Task<string?> GetFileContentAsync(string fileReference)
	{
		var (category, fileName) = ParseFileReference(fileReference);
		var filePath = FindFilePath(category, fileName);

		if (filePath == null || !File.Exists(filePath))
		{
			return null;
		}

		var content = await File.ReadAllTextAsync(filePath);
		return content;
	}

	public async Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern)
	{
		await _initializationTask.WithCancellation(CancellationToken.None);

		var category = ResolveEntryCategory(fileReference);
		// The general MUSH wildcard: case-insensitive, \ making the next character literal.
		var regex = SoftcodeRegex.Wildcard(pattern);

		lock (_indexLock)
		{
			IEnumerable<string> entries;

			if (category != null && !_categoryIndexes.ContainsKey(category))
			{
				return [];
			}
			if (category != null && _categoryIndexes.TryGetValue(category, out var categoryEntries))
			{
				entries = categoryEntries.Where(item => !item.Value.Hidden && SoftcodeRegex.IsMatch(regex, item.Key)).Select(item => item.Value.EntryName);
			}
			else
			{
				entries = _categoryIndexes.Values.SelectMany(dict => dict)
					.Where(item => !item.Value.Hidden && SoftcodeRegex.IsMatch(regex, item.Key))
					.Select(item => item.Value.EntryName);
			}

			return entries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}
	}

	public async Task<IEnumerable<string>> SearchContentAsync(string fileReference, string searchTerm)
	{
		await _initializationTask.WithCancellation(CancellationToken.None);

		var category = ResolveEntryCategory(fileReference);

		IEnumerable<KeyValuePair<string, IndexEntry>> entries;
		lock (_indexLock)
		{
			if (category != null && !_categoryIndexes.ContainsKey(category))
			{
				return [];
			}
			if (category != null && _categoryIndexes.TryGetValue(category, out var categoryEntries))
			{
				entries = categoryEntries.ToList();
			}
			else
			{
				entries = _categoryIndexes.Values
					.SelectMany(dict => dict)
					.GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.First())
					.ToList();
			}
		}

		var results = new List<string>();
		foreach (var indexEntry in entries.Select(pair => pair.Value).Where(entry => !entry.Hidden)
			.DistinctBy(entry => entry.EntryName, StringComparer.OrdinalIgnoreCase))
		{
			var content = indexEntry.Help?.Article is { } article
				? indexEntry.Help.SectionId is null ? article.Overview : article.Sections.First(section => section.Id == indexEntry.Help.SectionId).Markdown
				: indexEntry.Help?.Markdown ?? string.Empty;
			if (content.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
			{
				results.Add(indexEntry.EntryName);
			}
		}

		return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>
	/// Rebuilds the whole entry index from disk.
	/// </summary>
	/// <remarks>
	/// The replacement index is built entirely off to the side and swapped in under one lock. Clearing the
	/// live index first and refilling it category by category would leave it visibly empty, then visibly
	/// partial, for as long as the file reads take, and every concurrent <c>help</c> during that window would
	/// answer from whatever happened to be indexed so far. <c>@readcache</c> on a live game is exactly when
	/// the most people are reading help.
	/// </remarks>
	public async Task ReindexAsync()
	{
		var baseDir = _options.Value.TextFile.TextFilesDirectory;

		if (!Directory.Exists(baseDir))
		{
			_logger.LogWarning("Text files directory does not exist: {Directory}", baseDir);
			Directory.CreateDirectory(baseDir);
			return;
		}

		var categories = await ListCategoriesAsync();
		var rebuilt = new Dictionary<string, Dictionary<string, IndexEntry>>(StringComparer.OrdinalIgnoreCase);

		foreach (var category in categories.OrderBy(name => name, StringComparer.Ordinal))
		{
			var categoryIndex = BuildCategoryIndex(category);
			if (categoryIndex is not null)
			{
				rebuilt[category] = categoryIndex;
			}
		}

		lock (_indexLock)
		{
			_categoryIndexes.Clear();
			foreach (var (category, categoryIndex) in rebuilt)
			{
				_categoryIndexes[category] = categoryIndex;
			}
		}

		_logger.LogInformation("Indexed {Count} categories", rebuilt.Count);
	}

	/// <summary>
	/// Indexes one category's markdown files, or returns null when the directory has gone.
	/// Publishing the result is the caller's job — see <see cref="ReindexAsync"/>.
	/// </summary>
	private Dictionary<string, IndexEntry>? BuildCategoryIndex(string category)
	{
		var baseDir = _options.Value.TextFile.TextFilesDirectory;
		var categoryPath = Path.Combine(baseDir, category);

		if (!Directory.Exists(categoryPath))
		{
			return null;
		}

		var categoryIndex = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);

		var mdFiles = Directory.GetFiles(categoryPath, "*.md").OrderBy(path => path, StringComparer.Ordinal);

		foreach (var file in mdFiles)
		{
			IndexMarkdownFile(file, category, categoryIndex);
		}

		_logger.LogDebug("Indexed category {Category}: {Count} entries", category, categoryIndex.Count);
		return categoryIndex;
	}

	private void IndexMarkdownFile(string filePath, string category, Dictionary<string, IndexEntry> index)
	{
		var parsed = HelpArticleParser.Parse(File.ReadAllText(filePath), category);
		foreach (var source in parsed)
		{
			var article = source.Article;
			if (source.Declared && index.Values.Any(entry => entry.Help?.Article?.Id == article.Id))
			{
				throw new InvalidDataException($"Duplicate article ID in {category}: {article.Id}");
			}
			void Add(string lookup, HelpEntry help, bool hidden = false)
			{
				var entry = new IndexEntry(filePath, 0, 0, help.Topic, help, hidden);
				if (!index.TryAdd(lookup, entry))
				{
					if (source.Declared || index[lookup].Help?.Article?.Id.StartsWith("legacy:", StringComparison.Ordinal) == false)
					{
						throw new InvalidDataException($"Duplicate help lookup in {category}: {lookup}");
					}
					_logger.LogWarning("Duplicate legacy help lookup {Lookup} in {File}; keeping the first definition", lookup, filePath);
				}
			}
			Add(article.Lookup, article.Entry());
			foreach (var alias in article.Aliases)
			{
				Add(alias, article.Entry());
			}
			foreach (var section in article.Sections)
			{
				Add(section.Lookup, article.Entry(section));
				foreach (var alias in section.Aliases)
				{
					Add(alias, article.Entry(section));
				}
			}
			foreach (var (alias, target) in source.Redirects)
			{
				Add(alias, index[target].Help!, true);
			}
		}
	}

	/// <summary>
	/// When multiple consecutive markdown headers appear at the start of content
	/// (from aliased help topics), keep only the first header line.
	/// </summary>
	public static string StripConsecutiveHeaders(string content)
	{
		var text = content.AsSpan();
		var headers = 0;
		var firstHeaderLength = 0;
		var afterHeaders = 0;

		// The leading run of header lines, up to the first line that is not one.
		foreach (var range in text.Split('\n'))
		{
			var (offset, length) = range.GetOffsetAndLength(text.Length);
			if (!text.Slice(offset, length).StartsWith("# "))
			{
				break;
			}

			if (headers++ == 0)
			{
				firstHeaderLength = length;
			}

			afterHeaders = Math.Min(offset + length + 1, text.Length);
		}

		return headers > 1
			? string.Concat(text[..firstHeaderLength], "\n", text[afterHeaders..])
			: content;
	}

	/// <summary>
	/// Resolves the category an <em>entry</em> lookup is scoped to. A bare reference naming an
	/// existing category is that category.
	/// </summary>
	/// <remarks>
	/// Entry lookups never use the filename half of a reference — entries are keyed by the topic
	/// headers inside the markdown, and one category's entries are indexed together regardless of
	/// which file they came from. So <c>GetEntryAsync("help", …)</c> used to parse as
	/// <c>(category: null, file: "help")</c> and fall through to "search every category", which let
	/// <c>help Security</c> answer out of the wizard-only <c>ahelp</c> corpus. Resolving a bare
	/// reference to the category of that name is what every caller — the HELP/NEWS/AHELP commands
	/// and PennMUSH's <c>textfile()</c>/<c>textentries()</c> — already means by it. An unrecognised
	/// reference still searches every category, which is the only way <c>textfile()</c> can be
	/// asked for something outside the shipped set.
	/// </remarks>
	private string? ResolveEntryCategory(string fileReference)
	{
		var (category, fileName) = ParseFileReference(fileReference);
		if (category is not null)
		{
			return category;
		}

		if (fileName is null)
		{
			return null;
		}

		lock (_indexLock)
		{
			return _categoryIndexes.ContainsKey(fileName) || fileName.Split('.')[0] is "help" or "ahelp" or "news"
				? fileName : null;
		}
	}

	private (string? Category, string? FileName) ParseFileReference(string fileReference)
	{
		if (string.IsNullOrEmpty(fileReference))
		{
			return (null, null);
		}

		var parts = fileReference.Split('/', 2);
		if (parts.Length == 2)
		{
			return (parts[0], parts[1]);
		}

		return (null, parts[0]);
	}

	private string? FindFilePath(string? category, string? fileName)
	{
		if (fileName == null)
		{
			return null;
		}

		var baseDir = _options.Value.TextFile.TextFilesDirectory;

		if (category != null)
		{
			var categoryPath = Path.Combine(baseDir, category);
			var filePath = Path.Combine(categoryPath, fileName);
			return File.Exists(filePath) ? filePath : null;
		}

		var categories = Directory.GetDirectories(baseDir);
		foreach (var cat in categories)
		{
			var filePath = Path.Combine(cat, fileName);
			if (File.Exists(filePath))
			{
				return filePath;
			}
		}

		return null;
	}
}
