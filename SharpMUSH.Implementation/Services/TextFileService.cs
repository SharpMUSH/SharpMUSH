using DotNext.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
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
	private readonly PluginCatalog? _plugins;
	private readonly Lazy<IPluginManager>? _pluginManager;

	private readonly Dictionary<string, Dictionary<string, IndexEntry>> _categoryIndexes = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _indexLock = new();
	private readonly AsyncLazy<bool> _initializationTask;

	/// <param name="plugins">
	/// The plugins this start loaded. Each may ship help of its own in <c>&lt;plugin folder&gt;/&lt;category&gt;/*.md</c>, which
	/// is indexed with the engine's while the plugin is loaded.
	/// </param>
	/// <param name="pluginManager">
	/// The running plugins. A plugin unloaded since the start leaves the catalog's report as it was, so whether it was is
	/// asked here; the manager reindexes when one is unloaded or reloaded.
	/// </param>
	public TextFileService(
		IOptions<SharpMUSHOptions> options,
		ILogger<TextFileService> logger,
		PluginCatalog? plugins = null,
		Lazy<IPluginManager>? pluginManager = null)
	{
		_options = options;
		_logger = logger;
		_plugins = plugins;
		_pluginManager = pluginManager;

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

	/// <summary>The configured text-files directory, resolved (see <see cref="ResolveDirectory"/>).</summary>
	private string BaseDirectory => ResolveDirectory(_options.Value.TextFile.TextFilesDirectory, AppContext.BaseDirectory);

	/// <summary>
	/// A relative <paramref name="configured"/> directory is the one beside the server binary, where the build
	/// copies the help files — not the process's working directory, which is the repository root under
	/// <c>dotnet run --project</c> and only happens to be the binary's folder in the Docker image. An absolute
	/// setting is used as given.
	/// </summary>
	public static string ResolveDirectory(string configured, string serverDirectory) =>
		Path.IsPathRooted(configured) ? configured : Path.Join(serverDirectory, configured);

	public Task<IEnumerable<string>> ListCategoriesAsync()
	{
		var baseDir = BaseDirectory;
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
		var baseDir = BaseDirectory;

		if (category != null)
		{
			if (PathInside(baseDir, category) is not { } categoryPath || !Directory.Exists(categoryPath))
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
		var baseDir = BaseDirectory;

		if (!Directory.Exists(baseDir))
		{
			_logger.LogWarning("Text files directory does not exist: {Directory}", baseDir);
			Directory.CreateDirectory(baseDir);
			return;
		}

		var categories = (await ListCategoriesAsync())
			.Concat(LoadedPlugins().SelectMany(plugin => Directory.GetDirectories(plugin.Directory).Select(Path.GetFileName)).OfType<string>())
			.Distinct(StringComparer.OrdinalIgnoreCase);
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
		var categoryPath = PathInside(BaseDirectory, category);
		var pluginPaths = LoadedPlugins()
			.Select(plugin => (Plugin: plugin, Path: PathInside(plugin.Directory, category)))
			.Where(item => item.Path is not null && Directory.Exists(item.Path))
			.ToList();
		if ((categoryPath is null || !Directory.Exists(categoryPath)) && pluginPaths.Count == 0)
		{
			return null;
		}

		var categoryIndex = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);

		if (categoryPath is not null && Directory.Exists(categoryPath))
		{
			foreach (var file in Directory.GetFiles(categoryPath, "*.md").OrderBy(path => path, StringComparer.Ordinal))
			{
				IndexMarkdownFile(file, category, categoryIndex);
			}
		}

		var pluginHelp = new List<(PluginBootEntry Plugin, HelpArticleParser.ParsedArticle Article)>();
		foreach (var (plugin, path) in pluginPaths)
		{
			foreach (var file in Directory.GetFiles(path!, "*.md").OrderBy(file => file, StringComparer.Ordinal))
			{
				pluginHelp.AddRange(IndexPluginFile(file, category, ref categoryIndex).Select(article => (plugin, article)));
			}
		}
		ListPluginHelp(category, categoryIndex, pluginHelp);

		_logger.LogDebug("Indexed category {Category}: {Count} entries", category, categoryIndex.Count);
		return categoryIndex;
	}

	/// <summary>The plugins this start loaded and not unloaded since, in load order.</summary>
	private IEnumerable<PluginBootEntry> LoadedPlugins() =>
		_plugins?.Entries
			.Where(entry => entry.Status == PluginBootStatus.Loaded && Directory.Exists(entry.Directory)
				&& _pluginManager?.Value.WasUnloaded(entry.Id) != true) ?? [];

	/// <summary>
	/// Indexes a plugin's help file into a copy of the index and keeps the copy only when the whole file indexed. A
	/// plugin file with a topic that clashes with one already indexed, even an old-style one, or a file that does not
	/// parse, is left out whole and logged; the engine's help, and the server, carry on without it.
	/// </summary>
	/// <returns>The file's articles, or none when it was left out.</returns>
	private IReadOnlyList<HelpArticleParser.ParsedArticle> IndexPluginFile(string filePath, string category,
		ref Dictionary<string, IndexEntry> index)
	{
		var attempt = new Dictionary<string, IndexEntry>(index, StringComparer.OrdinalIgnoreCase);
		try
		{
			var parsed = IndexMarkdownFile(filePath, category, attempt, plugin: true);
			index = attempt;
			return parsed;
		}
		catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
		{
			_logger.LogWarning("Plugin help {File} was not indexed: {Reason}", filePath, exception.Message);
			return [];
		}
	}

	/// <summary>The topic that lists the help of every running plugin, whether or not it is listed elsewhere.</summary>
	public const string PluginHelpLookup = "plugin help";

	/// <summary>
	/// Lists the plugins' help on the engine's index topics, so a reader browsing <c>help</c> finds it: every plugin
	/// topic on <see cref="PluginHelpLookup"/> under its plugin's name, and a topic that declares <c>listedIn</c> on
	/// each index it names. A listing whose index is not in this category is logged and left out; the topic itself
	/// stays indexed.
	/// </summary>
	private void ListPluginHelp(string category, Dictionary<string, IndexEntry> index,
		IReadOnlyList<(PluginBootEntry Plugin, HelpArticleParser.ParsedArticle Article)> help)
	{
		var lines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		void Note(string target, string line) =>
			(lines.TryGetValue(target, out var existing) ? existing : lines[target] = []).Add(line);

		foreach (var plugin in help.GroupBy(item => item.Plugin))
		{
			Note(PluginHelpLookup, $"{plugin.Key.Name ?? plugin.Key.Id}: "
				+ string.Join(", ", plugin.Select(item => $"[{item.Article.Article.Lookup}]")));
		}
		foreach (var listing in help.SelectMany(item => item.Article.Listings))
		{
			Note(listing.Index, $"[{listing.Lookup}]");
		}

		foreach (var (target, listed) in lines)
		{
			if (!index.TryGetValue(target, out var entry) || entry.Hidden || entry.Help?.Article is not { } article)
			{
				_logger.LogWarning("Plugin help for {Topics} names {Index}, which is not a {Category} topic",
					string.Join(", ", listed), target, category);
				continue;
			}
			var addition = target.Equals(PluginHelpLookup, StringComparison.OrdinalIgnoreCase)
				? "\n\n" + string.Join("<br>\n", listed)
				: "\n\nFrom plugins: " + string.Join(", ", listed);
			var listing = entry.Help.SectionId is { } sectionId
				? article with
				{
					Sections = article.Sections.Select(section => section.Id == sectionId
						? section with { Markdown = section.Markdown + addition }
						: section).ToList()
				}
				: article with { Overview = article.Overview + addition };
			foreach (var (lookup, existing) in index.Where(pair => pair.Value.Help?.Article?.Id == article.Id).ToList())
			{
				var section = listing.Sections.FirstOrDefault(section => section.Id == existing.Help!.SectionId);
				index[lookup] = existing with { Help = listing.Entry(section) };
			}
		}
	}

	/// <param name="plugin">Whether the file is a plugin's, which may not repeat a lookup even in an old-style file.</param>
	private IReadOnlyList<HelpArticleParser.ParsedArticle> IndexMarkdownFile(string filePath, string category,
		Dictionary<string, IndexEntry> index, bool plugin = false)
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
					if (plugin || source.Declared || index[lookup].Help?.Article?.Id.StartsWith("legacy:", StringComparison.Ordinal) == false)
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
		return parsed;
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

		var baseDir = BaseDirectory;

		if (category != null)
		{
			return PathInside(baseDir, category, fileName) is { } filePath && File.Exists(filePath) ? filePath : null;
		}

		var categories = Directory.GetDirectories(baseDir);
		foreach (var cat in categories)
		{
			if (PathInside(baseDir, Path.GetFileName(cat), fileName) is { } filePath && File.Exists(filePath))
			{
				return filePath;
			}
		}

		return null;
	}

	/// <summary>
	/// <paramref name="names"/> joined under <paramref name="baseDir"/>, or null when the result is not strictly
	/// inside it. The names come from a caller's file reference (<c>textfile()</c>, help), so a <c>..</c> or a
	/// rooted name must not reach a file beside the text-files directory. <see cref="Path.Combine(string[])"/>
	/// would let a rooted name replace the directory outright, hence the rooted check before joining.
	/// </summary>
	internal static string? PathInside(string baseDir, params ReadOnlySpan<string> names)
	{
		foreach (var name in names)
		{
			if (Path.IsPathRooted(name))
			{
				return null;
			}
		}

		var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir)) + Path.DirectorySeparatorChar;
		var full = Path.GetFullPath(Path.Join(root, string.Join(Path.DirectorySeparatorChar, names)));
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		return full.Length > root.Length && full.StartsWith(root, comparison) ? full : null;
	}
}
