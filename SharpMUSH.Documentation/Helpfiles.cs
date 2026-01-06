using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;

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
		var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
		
		return IndexedHelp.Keys.Where(k => regex.IsMatch(k));
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
		// Index .txt files
		var txtFiles = directory.GetFiles("*.txt");
		foreach (var file in txtFiles)
		{
			 var maybeIndexedFile = Index(file);
			 if (maybeIndexedFile.IsT1)
			 {
				 logger?.LogWarning("Failed to index helpfile {FilePath}: {Error}", file.FullName, maybeIndexedFile.AsT1.Value);
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

	public static OneOf<Dictionary<string,string>, Error<string>> Index(FileInfo file)
	{
		if (!file.Exists)
		{
			return new Error<string>($"File {file.FullName} does not exist.");
		}

		var dict = new Dictionary<string, string>();

		using var openText = file.OpenText();
		
		var textBody = openText.ReadToEnd().Replace("\r\n","\n");
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

		foreach (Match match in matches)
		{
			var topicName = match.Groups["Topic"].Value.Trim();
			var startIndex = match.Index + match.Length;
			
			// Find the end of this topic (next header or end of file)
			var nextMatch = match.NextMatch();
			var endIndex = nextMatch.Success ? nextMatch.Index : textBody.Length;
			
			// Extract the content between this header and the next
			var content = textBody.Substring(startIndex, endIndex - startIndex).Trim();
			
			// Include the header in the content
			var fullContent = match.Value + content;
			
			dict[topicName] = fullContent;
		}

		return dict;
	}
	
	[GeneratedRegex(@"^# (?<Topic>.+)$", RegexOptions.Compiled | RegexOptions.Multiline)]
	private static partial Regex MarkdownHeaders();
}