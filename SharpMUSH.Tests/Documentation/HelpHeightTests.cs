using SharpMUSH.Documentation;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Whether every help entry fits on a telnet screen. PennMUSH splits its topics so a reader on an 80 by 24
/// terminal never has one scroll away: of its 1,123 entries, 26 run past 30 rows. Here an entry is a legacy topic,
/// a declared article's overview, or one of its sections, and it is measured as the terminal shows it, with each
/// line wrapped at 78 columns.
/// </summary>
public class HelpHeightTests
{
	private const int Rows = 30;
	private const int Columns = 78;

	public static int Height(HelpEntry entry) =>
		RecursiveMarkdownHelper.RenderMarkdown(entry.Markdown).ToPlainText().Split('\n')
			.Sum(line => Math.Max(1, (line.Length + Columns - 1) / Columns));

	[Test]
	public async Task EveryHelpEntryFitsOnATelnetScreen()
	{
		var tall = TestPaths.Helpfiles
			.EnumerateFiles("*.md", SearchOption.AllDirectories)
			.SelectMany(file => HelpArticleParser.Parse(File.ReadAllText(file.FullName),
					file.Directory!.Name is "ahelp" or "news" ? file.Directory.Name : "help")
				.SelectMany(parsed => parsed.Article.Sections.Select(parsed.Article.Entry).Prepend(parsed.Article.Entry()))
				.Select(entry => (File: file.Name, entry.Topic, Height: Height(entry))))
			.Where(entry => entry.Height > Rows)
			.OrderByDescending(entry => entry.Height)
			.Select(entry => $"{entry.File}: {entry.Topic} ({entry.Height} rows)")
			.ToList();

		await Assert.That(string.Join("\n", tall)).IsEmpty();
	}
}
