using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Index entry with file position metadata for efficient reads
/// </summary>
public record IndexEntry(
	string FilePath,
	long StartPosition,
	long EndPosition,
	string EntryName
);

public class TextFileService : ITextFileService
{
	private readonly IOptions<SharpMUSHOptions> _options;
	private readonly ILogger<TextFileService> _logger;
	
	// Category -> (EntryName -> IndexEntry with file position)
	private readonly Dictionary<string, Dictionary<string, IndexEntry>> _categoryIndexes = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _indexLock = new();
	private readonly Task _initializationTask;

	public TextFileService(
		IOptions<SharpMUSHOptions> options,
		ILogger<TextFileService> logger)
	{
		_options = options;
		_logger = logger;

		if (_options.Value.TextFile.CacheOnStartup)
		{
			_initializationTask = Task.Run(async () => await ReindexAsync());
		}
		else
		{
			_initializationTask = Task.CompletedTask;
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
		await _initializationTask;
		
		var (category, _) = ParseFileReference(fileReference);
		
		lock (_indexLock)
		{
			if (category != null && _categoryIndexes.TryGetValue(category, out var entries))
			{
				return string.Join(separator, entries.Keys.OrderBy(k => k));
			}

			// Search all categories
			var allEntries = _categoryIndexes.Values
				.SelectMany(dict => dict.Keys)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(k => k);
			
			return string.Join(separator, allEntries);
		}
	}

	public async Task<string?> GetEntryAsync(string fileReference, string entryName)
	{
		await _initializationTask;
		
		var (category, _) = ParseFileReference(fileReference);
		
		IndexEntry? indexEntry = null;
		lock (_indexLock)
		{
			if (category != null && _categoryIndexes.TryGetValue(category, out var categoryEntries))
			{
				categoryEntries.TryGetValue(entryName, out indexEntry);
			}
			else
			{
				// Search all categories
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

		return await ReadEntryFromFileAsync(indexEntry);
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
				.Select(Path.GetFileName)
				.Where(f => f != null)
				.Cast<string>();
			return Task.FromResult(files);
		}
		else
		{
			var files = Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories)
				.Select(Path.GetFileName)
				.Where(f => f != null)
				.Cast<string>()
				.Distinct();
			return Task.FromResult(files);
		}
	}

	public Task<string?> GetFileContentAsync(string fileReference)
	{
		var (category, fileName) = ParseFileReference(fileReference);
		var filePath = FindFilePath(category, fileName);
		
		if (filePath == null || !File.Exists(filePath))
		{
			return Task.FromResult<string?>(null);
		}

		var content = File.ReadAllText(filePath);
		return Task.FromResult<string?>(content);
	}

	public async Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern)
	{
		await _initializationTask;
		
		var (category, _) = ParseFileReference(fileReference);
		var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
		var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

		lock (_indexLock)
		{
			IEnumerable<string> entries;
			
			if (category != null && _categoryIndexes.TryGetValue(category, out var categoryEntries))
			{
				entries = categoryEntries.Keys;
			}
			else
			{
				entries = _categoryIndexes.Values
					.SelectMany(dict => dict.Keys)
					.Distinct(StringComparer.OrdinalIgnoreCase);
			}

			return entries.Where(e => regex.IsMatch(e)).ToList();
		}
	}

	public async Task ReindexAsync()
	{
		var baseDir = _options.Value.TextFile.TextFilesDirectory;
		
		if (!Directory.Exists(baseDir))
		{
			_logger.LogWarning("Text files directory does not exist: {Directory}", baseDir);
			Directory.CreateDirectory(baseDir);
			return;
		}

		lock (_indexLock)
		{
			_categoryIndexes.Clear();
		}

		var categories = await ListCategoriesAsync();
		
		foreach (var category in categories)
		{
			await IndexCategoryAsync(category);
		}

		_logger.LogInformation("Indexed {Count} categories", _categoryIndexes.Count);
	}

	private async Task IndexCategoryAsync(string category)
	{
		var baseDir = _options.Value.TextFile.TextFilesDirectory;
		var categoryPath = Path.Combine(baseDir, category);
		
		if (!Directory.Exists(categoryPath))
		{
			return;
		}

		var categoryIndex = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
		
		// Index only .md files
		var mdFiles = Directory.GetFiles(categoryPath, "*.md");

		foreach (var file in mdFiles)
		{
			await IndexMarkdownFileAsync(file, categoryIndex);
		}

		lock (_indexLock)
		{
			_categoryIndexes[category] = categoryIndex;
		}

		_logger.LogDebug("Indexed category {Category}: {Count} entries", category, categoryIndex.Count);
	}

	private async Task IndexMarkdownFileAsync(string filePath, Dictionary<string, IndexEntry> index)
	{
		var fileInfo = new FileInfo(filePath);
		var result = Helpfiles.IndexMarkdown(fileInfo);
		
		if (result.IsT1)
		{
			_logger.LogWarning("Failed to index markdown {File}: {Error}", filePath, result.AsT1.Value);
			return;
		}

		var entries = result.AsT0;
		var content = await File.ReadAllTextAsync(filePath);
		
		foreach (var (entryName, entryContent) in entries)
		{
			// For markdown files, find the position of the header
			var headerPattern = $"# {Regex.Escape(entryName)}";
			var match = Regex.Match(content, headerPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
			
			if (match.Success)
			{
				var startPos = match.Index;
				// Find next header or end of file
				var nextHeaderMatch = Regex.Match(content.Substring(startPos + match.Length), @"^# ", RegexOptions.Multiline);
				var endPos = nextHeaderMatch.Success 
					? startPos + match.Length + nextHeaderMatch.Index 
					: content.Length;

				var entry = new IndexEntry(
					filePath,
					startPos,
					endPos,
					entryName
				);
				index[entryName] = entry;
			}
		}
	}

	private async Task<string> ReadEntryFromFileAsync(IndexEntry entry)
	{
		var length = (int)(entry.EndPosition - entry.StartPosition);
		var buffer = ArrayPool<byte>.Shared.Rent(length);

		try
		{
			using var fileStream = new FileStream(
				entry.FilePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				bufferSize: 4096,
				useAsync: true);

			fileStream.Seek(entry.StartPosition, SeekOrigin.Begin);
			var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, length));

			return Encoding.UTF8.GetString(buffer.AsSpan(0, bytesRead));
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
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

		// Search all categories
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
