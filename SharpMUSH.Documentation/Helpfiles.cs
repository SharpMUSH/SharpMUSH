using System.Text.RegularExpressions;
using OneOf;
using OneOf.Types;

namespace SharpMUSH.Documentation;

public partial class Helpfiles(DirectoryInfo directory)
{
	public Dictionary<string, string> IndexedHelp { get; } = [];

	public void Index()
	{
		var files = directory.GetFiles("*.hlp");
		
		foreach (var file in files)
		{
			 var maybeIndexedFile = Index(file);
			 if (maybeIndexedFile.IsT1)
			 {
				 // TODO: LOGGING
				 continue;
			 }

			 var indexedFile = maybeIndexedFile.AsT0;
			 
			 foreach (var kv in indexedFile)
			 {
				 if (IndexedHelp.ContainsKey(kv.Key))
				 {
					 // TODO: LOGGING
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