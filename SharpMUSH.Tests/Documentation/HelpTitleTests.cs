using SharpMUSH.Documentation;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// A help title is the header a reader sees above the topic, so every title follows one style.
/// Commands and functions are written the way they are typed (<c>@mail/read</c>, <c>strlen()</c>),
/// and a topic about an idea is in Title Case (<c>Attribute Trees</c>), never all capitals or all
/// lowercase. A one-word title is left alone: it may be a command (<c>look</c>), a flag or attribute
/// the game spells in capitals (<c>WIZARD</c>), or a word in Title Case (<c>Zones</c>).
/// </summary>
public class HelpTitleTests
{
	private static readonly HashSet<string> SmallWords =
		["a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to", "via", "vs", "with"];

	private static readonly HashSet<string> Acronyms =
		["ANSI", "DB", "GMCP", "GS", "HTML", "HTTP", "JSON", "MSSP", "MUSH", "MUX", "MXP", "SQL"];

	private static bool IsStyled(string title)
	{
		if (title.EndsWith("()", StringComparison.Ordinal) || title.StartsWith('@'))
		{
			return title == title.ToLowerInvariant();
		}
		var words = title.Split(' ');
		if ("%&$#".Contains(title[0]) || !title.Any(char.IsLetter) || words.Length == 1)
		{
			return true;
		}
		return words.Select((word, index) => (word, index))
			.Where(entry => char.IsLetter(entry.word[0]))
			.All(entry => entry.word.Any(char.IsLower)
				? char.IsUpper(entry.word[0]) || (entry.index > 0 && SmallWords.Contains(entry.word))
				: Acronyms.Contains(entry.word.TrimEnd(':')));
	}

	[Test]
	[Arguments("Attribute Trees", true)]
	[Arguments("Types of Objects", true)]
	[Arguments("JSON Functions", true)]
	[Arguments("@mail/read", true)]
	[Arguments("strlen()", true)]
	[Arguments("WIZARD", true)]
	[Arguments("look", true)]
	[Arguments("attribute trees", false)]
	[Arguments("COMPATIBILITY ARGUMENTS", false)]
	[Arguments("Attribute functions", false)]
	[Arguments("@MAIL/READ", false)]
	[Arguments("STRLEN()", false)]
	public async Task TitleStyle(string title, bool styled) =>
		await Assert.That(IsStyled(title)).IsEqualTo(styled);

	[Test]
	public async Task EveryHelpTitleFollowsTheStyle()
	{
		var offending = TestPaths.Helpfiles
			.EnumerateFiles("*.md", SearchOption.AllDirectories)
			.SelectMany(file => HelpArticleParser.Parse(File.ReadAllText(file.FullName),
					file.Directory!.Name is "ahelp" or "news" ? file.Directory.Name : "help")
				.Where(parsed => !IsStyled(parsed.Article.Title))
				.Select(parsed => $"{file.Name}: {parsed.Article.Title}"))
			.ToList();

		await Assert.That(offending).IsEmpty();
	}
}
