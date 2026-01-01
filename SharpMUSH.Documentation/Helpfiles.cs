using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;

namespace SharpMUSH.Documentation;

public partial class Helpfiles(DirectoryInfo directory, ILogger<Helpfiles>? logger = null)
{
	public Dictionary<string, string> IndexedHelp { get; } = [];

	public void Index()
	{
		var files = directory.GetFiles("*.txt");
		
		foreach (var file in files)
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
}