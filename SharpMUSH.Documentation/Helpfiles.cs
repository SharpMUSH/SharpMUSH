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
		
		var textBody = openText.ReadToEnd();
		var matches = Indexes().Matches(textBody);

		Console.WriteLine($"STARTING INDEX MATCHING FOR {matches.Count} MATCHES");
		foreach (Match match in matches)
		{
			Console.WriteLine("INDEX MATCH");
			var indexes = match.Groups["Indexes"].Captures.Select(x => x.Value);
			var body = match.Groups["Body"].Value;
			Console.WriteLine($"INDEX MATCH INDEXES: {string.Join(", ", indexes)}");
			Console.WriteLine($"INDEX MATCH BODY: {body}");

			foreach (var index in indexes)
			{
				Console.WriteLine($"INDEX MATCH ADDING: {index}");
				dict.Add(index, body);
			}
		}

		return dict;
	}

	[GeneratedRegex(@"(?:^& (?<Indexes>.+)\r\n)+(?<Body>(?:[^&].*\n)+)", RegexOptions.Compiled | RegexOptions.Multiline)]
	private static partial Regex Indexes();
}