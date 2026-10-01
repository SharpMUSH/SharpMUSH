using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation;

public partial class Helpfiles(DirectoryInfo directory, ILogger<Helpfiles>? logger = null)
{
	public Dictionary<string, string> IndexedHelp { get; } = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HelpEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _redirects = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Retrieves canonical article and section identity for exporters.</summary>
	public HelpEntry? FindHelpEntry(string topic) => _entries.GetValueOrDefault(topic);

	/// <summary>
	/// Finds a help entry by exact match or wildcard pattern
	/// </summary>
	public string? FindEntry(string topic)
	{
		if (IndexedHelp.TryGetValue(topic, out var content))
		{
			return content;
		}

		return null;
	}

	/// <summary>
	/// Finds all help entries whose topic matches <paramref name="pattern"/>, a general MUSH wildcard
	/// (<see cref="SoftcodeRegex.Wildcard"/>): case-insensitive, with <c>\</c> making the next character literal.
	/// </summary>
	public IEnumerable<string> FindMatchingTopics(string pattern)
	{
		var regex = SoftcodeRegex.Wildcard(pattern);
		return _entries.Where(pair => !_redirects.Contains(pair.Key) && SoftcodeRegex.IsMatch(regex, pair.Key))
			.Select(pair => pair.Value.Topic).Distinct(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Searches help content for entries containing the search term
	/// </summary>
	public IEnumerable<string> SearchContent(string searchTerm)
	{
		return _entries.Where(pair => !_redirects.Contains(pair.Key)).Select(pair => pair.Value)
			.DistinctBy(entry => entry.Topic, StringComparer.OrdinalIgnoreCase)
			.Where(entry => (entry.Article is { } article
				? entry.SectionId is null ? article.Overview : article.Sections.First(section => section.Id == entry.SectionId).Markdown
				: entry.Markdown).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
			.Select(entry => entry.Topic);
	}

	public void Index()
	{
		IndexedHelp.Clear();
		_entries.Clear();
		_redirects.Clear();
		IndexMarkdownFilesRecursive(directory);
	}

	private void IndexMarkdownFilesRecursive(DirectoryInfo dir)
	{
		foreach (var file in dir.GetFiles("*.md").OrderBy(file => file.Name, StringComparer.Ordinal))
		{
			var corpus = dir.Name.Split('.')[0] is "ahelp" or "news" ? dir.Name.Split('.')[0] : "help";
			foreach (var parsed in HelpArticleParser.Parse(File.ReadAllText(file.FullName), corpus))
			{
				var article = parsed.Article;
				void Add(string name, HelpEntry entry, bool redirect = false)
				{
					if (!_entries.TryAdd(name, entry))
					{
						logger?.LogWarning("Duplicate help index {Lookup} in {File}; keeping the first definition", name, file.FullName);
						return;
					}
					IndexedHelp.Add(name, entry.Markdown);
					if (redirect)
					{
						_redirects.Add(name);
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
				foreach (var (alias, target) in parsed.Redirects)
				{
					Add(alias, _entries[target], true);
				}
			}
		}
		foreach (var subDir in dir.GetDirectories().OrderBy(dir => dir.Name, StringComparer.Ordinal))
		{
			IndexMarkdownFilesRecursive(subDir);
		}
	}

	public static Result<Dictionary<string, string>> Index(FileInfo file)
	{
		if (!file.Exists)
		{
			return new Error<string>($"File {file.FullName} does not exist.");
		}

		var dict = new Dictionary<string, string>();

		using var openText = file.OpenText();

		var textBody = openText.ReadToEnd().Replace("\r\n", "\n");
		var matches = Indexes().Matches(textBody);

		foreach (Match match in matches)
		{
			var indexes = match.Groups["Indexes"].Captures.Select(x => x.Value.Trim());
			var body = match.Groups["Body"].Value;

			foreach (var index in indexes)
			{
				dict.Add(index, body);
			}
		}

		return dict;
	}

	[GeneratedRegex(@"(?:^& (?<Indexes>.+)\n)+(?<Body>(?:[^&].*\n)+)", RegexOptions.Compiled | RegexOptions.Multiline)]
	private static partial Regex Indexes();

	public static Result<Dictionary<string, string>> IndexMarkdown(FileInfo file)
	{
		if (!file.Exists)
		{
			return new Error<string>($"File {file.FullName} does not exist.");
		}

		try
		{
			var markdown = File.ReadAllText(file.FullName);
			var directoryName = file.Directory?.Name.Split('.')[0];
			var corpus = directoryName is "ahelp" or "news" ? directoryName : "help";
			var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var parsed in HelpArticleParser.Parse(markdown, corpus))
			{
				var article = parsed.Article;
				entries.TryAdd(article.Lookup, article.Entry().Markdown);
				foreach (var alias in article.Aliases)
				{
					entries.TryAdd(alias, article.Entry().Markdown);
				}
				foreach (var section in article.Sections)
				{
					entries.Add(section.Lookup, article.Entry(section).Markdown);
					foreach (var alias in section.Aliases)
					{
						entries.Add(alias, article.Entry(section).Markdown);
					}
				}
				foreach (var (alias, target) in parsed.Redirects)
				{
					entries.Add(alias, entries[target]);
				}
			}
			return entries;
		}
		catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
		{
			return new Error<string>(error.Message);
		}
	}

	/// <summary>
	/// Indexes a markdown help file and returns byte positions for each entry.
	/// Consecutive headers without content between them are treated as aliases
	/// that all share the same byte range (from the first alias header to the
	/// end of the content block).
	/// </summary>
	public static Result<Dictionary<string, (long Start, long End)>> IndexMarkdownPositions(FileInfo file)
	{
		if (!file.Exists)
		{
			return new Error<string>($"File {file.FullName} does not exist.");
		}

		var dict = new Dictionary<string, (long Start, long End)>(StringComparer.OrdinalIgnoreCase);

		// Read raw bytes to ensure byte offsets correspond exactly to file positions.
		// Using StreamReader would silently strip BOM bytes, and replacing \r\n with \n
		// would shift all positions when files have CRLF line endings.
		var rawBytes = File.ReadAllBytes(file.FullName);
		var preamble = Encoding.UTF8.Preamble;
		var bomLength = rawBytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
		var textBody = Encoding.UTF8.GetString(rawBytes, bomLength, rawBytes.Length - bomLength);

		// Pre-compute cumulative byte offsets so we can convert any char index
		// to a byte position in O(1) instead of re-encoding prefixes.
		var byteOffsets = new int[textBody.Length + 1];
		var running = bomLength;
		for (var i = 0; i < textBody.Length; i++)
		{
			byteOffsets[i] = running;
			var charCount = char.IsHighSurrogate(textBody[i]) && i + 1 < textBody.Length && char.IsLowSurrogate(textBody[i + 1]) ? 2 : 1;
			running += Encoding.UTF8.GetByteCount(textBody.AsSpan(i, charCount));
			if (charCount == 2)
			{
				byteOffsets[++i] = byteOffsets[i - 1];
			}
		}
		byteOffsets[textBody.Length] = running;

		var matches = HelpArticleParser.Headings(textBody).Where(heading => heading.Level == 1).ToList();

		var pendingTopics = new List<string>();
		var firstPendingCharIndex = -1;

		for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
		{
			var match = matches[matchIndex];
			var topicName = HelpArticleParser.HeadingText(textBody, match);
			var startIndex = match.Span.End + 1;
			var endIndex = matchIndex + 1 < matches.Count ? matches[matchIndex + 1].Span.Start : textBody.Length;

			var content = textBody.AsSpan(startIndex, endIndex - startIndex).Trim();

			pendingTopics.Add(topicName);
			if (firstPendingCharIndex < 0)
			{
				firstPendingCharIndex = match.Span.Start;
			}

			if (!content.IsEmpty)
			{
				var startByte = (long)byteOffsets[firstPendingCharIndex];
				var endByte = (long)byteOffsets[endIndex];

				foreach (var topic in pendingTopics)
				{
					dict[topic] = (startByte, endByte);
				}

				pendingTopics.Clear();
				firstPendingCharIndex = -1;
			}
		}

		// Remaining pending topics had no content; store the header range.
		foreach (var topic in pendingTopics)
		{
			var charStart = firstPendingCharIndex >= 0 ? firstPendingCharIndex : 0;
			var startByte = (long)byteOffsets[charStart];
			var endByte = (long)byteOffsets[textBody.Length];
			dict[topic] = (startByte, endByte);
		}

		return dict;
	}

}
