namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// A code block shows what a player types and what the game prints, character for character, so nothing in
/// it is Markdown. The conversion from PennMUSH's help wrapped angle brackets in backticks or escaped them
/// with a backslash, which broke examples such as <c>%q&lt;one&gt;</c>: typed as shown, they no longer run.
/// </summary>
public class HelpExampleMarkupTests
{
	[Test]
	public async Task NoCodeBlockEscapesAnAngleBracket()
	{
		var offending = TestPaths.Helpfiles
			.EnumerateFiles("*.md", SearchOption.AllDirectories)
			.SelectMany(file => CodeBlockLines(File.ReadLines(file.FullName))
				.Where(entry => entry.Line.Contains("`<") || entry.Line.Contains(">`") || entry.Line.Contains("\\<"))
				.Select(entry => $"{file.Name}:{entry.Number}: {entry.Line}"))
			.ToList();

		await Assert.That(offending).IsEmpty();
	}

	private static IEnumerable<(string Line, int Number)> CodeBlockLines(IEnumerable<string> lines)
	{
		var inBlock = false;
		var number = 0;
		foreach (var line in lines)
		{
			number++;
			if (line.TrimStart().StartsWith("```"))
			{
				inBlock = !inBlock;
				continue;
			}

			if (inBlock) yield return (line, number);
		}
	}
}
