using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation;

public partial class Helpfiles(DirectoryInfo directory, ILogger<Helpfiles>? logger = null)
{
	public Dictionary<string, string> IndexedHelp { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Finds a help entry by exact match or wildcard pattern
	/// </summary>
	public string? FindEntry(string topic)
	{
		// Try exact match first (case-insensitive)
		if (IndexedHelp.TryGetValue(topic, out var content))
		{
			return content;
		}

		return null;
	}

	/// <summary>
	/// Finds all help entries that match a wildcard pattern
	/// </summary>
	public IEnumerable<string> FindMatchingTopics(string pattern)
	{
		// Convert wildcard pattern to regex
		var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
		var compiledRegex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

		return IndexedHelp.Keys.Where(k => compiledRegex.IsMatch(k));
	}

	/// <summary>
	/// Searches help content for entries containing the search term
	/// </summary>
	public IEnumerable<string> SearchContent(string searchTerm)
	{
		return IndexedHelp
			.Where(kv => kv.Value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
			.Select(kv => kv.Key);
	}

	public void Index()
	{
		// Index .md files recursively
		IndexMarkdownFilesRecursive(directory);
	}

	private void IndexMarkdownFilesRecursive(DirectoryInfo dir)
	{
		var mdFiles = dir.GetFiles("*.md");
		foreach (var file in mdFiles)
		{
			var maybeIndexedFile = IndexMarkdown(file);
			if (maybeIndexedFile.IsT1)
			{
				logger?.LogWarning("Failed to index markdown helpfile {FilePath}: {Error}", file.FullName, maybeIndexedFile.AsT1.Value);
				continue;
			}

			var indexedFile = maybeIndexedFile.AsT0;

			foreach (var kv in indexedFile)
			{
				if (IndexedHelp.ContainsKey(kv.Key))
				{
					logger?.LogWarning("Duplicate help index '{HelpIndex}' found in file {FilePath}, skipping", kv.Key, file.FullName);
					continue;
				}
				IndexedHelp.Add(kv.Key, kv.Value);
			}
		}

		// Recursively index subdirectories
		foreach (var subDir in dir.GetDirectories())
		{
			IndexMarkdownFilesRecursive(subDir);
		}
	}

	public static OneOf<Dictionary<string, string>, Error<string>> Index(FileInfo file)
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

	public static OneOf<Dictionary<string, string>, Error<string>> IndexMarkdown(FileInfo file)
	{
		if (!file.Exists)
		{
			return new Error<string>($"File {file.FullName} does not exist.");
		}

		var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		using var openText = file.OpenText();
		var textBody = openText.ReadToEnd().Replace("\r\n", "\n");

		// Match markdown headers: # Topic Name
		var matches = MarkdownHeaders().Matches(textBody);

		// Track consecutive headers (aliases) that share the same content block.
		// When a header has no content before the next header, it is treated as an
		// alias for the next topic that does have content.
		var pendingTopics = new List<string>();
		var firstPendingHeaderText = (string?)null;

		foreach (Match match in matches)
		{
			var topicName = match.Groups["Topic"].Value.Trim();
			var startIndex = match.Index + match.Length;

			// Find the end of this topic (next header or end of file)
			var nextMatch = match.NextMatch();
			var endIndex = nextMatch.Success ? nextMatch.Index : textBody.Length;

			// Extract the content between this header and the next
			var content = textBody.Substring(startIndex, endIndex - startIndex).Trim();

			pendingTopics.Add(topicName);
			firstPendingHeaderText ??= match.Value;

			if (!string.IsNullOrEmpty(content))
			{
				// Include the first pending header in the content so that looking up any
				// alias shows the primary topic name at the top.
				var fullContent = firstPendingHeaderText + content;

				foreach (var topic in pendingTopics)
				{
					dict[topic] = fullContent;
				}

				pendingTopics.Clear();
				firstPendingHeaderText = null;
			}
		}

		// Any remaining pending topics had no content; store just the header for them.
		foreach (var topic in pendingTopics)
		{
			dict[topic] = "# " + topic;
		}

		return dict;
	}

	[GeneratedRegex(@"^# (?<Topic>.+)$", RegexOptions.Compiled | RegexOptions.Multiline)]
	private static partial Regex MarkdownHeaders();
}