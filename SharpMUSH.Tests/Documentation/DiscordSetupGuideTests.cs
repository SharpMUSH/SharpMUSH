using SharpMUSH.Documentation;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Tests.Documentation;

public class DiscordSetupGuideTests
{
	private const string Guide = "https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/guides/discord-setup.md";

	[Test]
	public async Task FocusedSetupAndItsConnectAliasRetainTheIllustratedGuideInBothRenderers()
	{
		var raw = await File.ReadAllTextAsync(Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Plugins.Discord", "help", "discord-command.md"));
		var article = HelpArticleParser.Parse(raw, "help").Single().Article;
		var setup = article.Sections.Single(section => section.Id == "discord-setup");
		await Assert.That(setup.Lookup).IsEqualTo("@discord setup");
		await Assert.That(setup.Aliases).Contains("@discord/connect");
		var markdown = article.Entry(setup).Markdown;
		await Assert.That(markdown).Contains(Guide);
		await Assert.That(HelpHtmlRenderer.RenderToHtml(markdown, topic => "/help/" + Uri.EscapeDataString(topic), article))
			.Contains("href=\"" + Guide + "\"");
		await Assert.That(RecursiveMarkdownHelper.RenderMarkdown(markdown, corpus: "help").ToPlainText()).Contains(Guide);
		await Assert.That(HelpHeightTests.Height(article.Entry(setup))).IsLessThanOrEqualTo(31);
	}
}
