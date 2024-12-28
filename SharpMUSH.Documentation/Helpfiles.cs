using System.Text.RegularExpressions;
using OneOf;
using OneOf.Types;

namespace SharpMUSH.Documentation;

public partial class Helpfiles(DirectoryInfo directory)
{
	public void Index()
	{
		var files = directory.GetFiles("*.hlp");
		foreach (var file in files)
		{
			Index(file);	
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