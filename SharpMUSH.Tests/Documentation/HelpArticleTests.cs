using SharpMUSH.Documentation;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Tests.Documentation;

public class HelpArticleTests
{
	private const string Article = """
		<!-- help-article
		{"corpus":"help","id":"sample","lookup":"sample()","aliases":["alternate()"],
		 "sections":[{"id":"examples","heading":"Examples","lookup":"sample examples"},
		 {"id":"options","heading":"Options","lookup":"sample options"}],
		 "redirects":{"SAMPLE2":"sample examples"}}
		-->
		# Sample
		Overview.
		## Options
		Option details.
		## Examples
		```sharp
		# This is code
		  significant   spacing
		```
		### Local explanation
		Example details.
		""";

	[Test]
	public async Task DeclaredOrderAndFocusedScopesShareIdentity()
	{
		var parsed = HelpArticleParser.Parse(Article, "help").Single();
		var article = parsed.Article;
		await Assert.That(article.Sections.Select(section => section.Id)).IsEquivalentTo(new[] { "examples", "options" });
		await Assert.That(article.Markdown.IndexOf("## Examples", StringComparison.Ordinal))
			.IsLessThan(article.Markdown.IndexOf("## Options", StringComparison.Ordinal));
		await Assert.That(article.Entry().Markdown).Contains("help sample examples");
		await Assert.That(article.Entry().Markdown).DoesNotContain("Option details");
		await Assert.That(article.Entry(article.Sections[0]).Markdown).Contains("Local explanation");
		await Assert.That(article.Entry(article.Sections[0]).Markdown).DoesNotContain("Option details");
		await Assert.That(article.Markdown).Contains("  significant   spacing");
		await Assert.That(parsed.Redirects["SAMPLE2"]).IsEqualTo("sample examples");
	}

	[Test]
	public async Task WebHasStableAnchorsAndOneCompleteArticle()
	{
		var article = HelpArticleParser.Parse(Article, "help").Single().Article;
		var html = HelpHtmlRenderer.RenderToHtml(article.Markdown, topic => "/help/" + Uri.EscapeDataString(topic), article);
		await Assert.That(html).Contains("id=\"examples\"");
		await Assert.That(html).Contains("href=\"/help/sample%28%29#examples\"");
		await Assert.That(html).Contains("aria-label=\"Article sections\"");
		await Assert.That(html).Contains("Option details");
		await Assert.That(html.Split("Example details").Length).IsEqualTo(2);
		await Assert.That(html).DoesNotContain("help-article");
	}

	[Test]
	public async Task TocLabelsUseParsedHeadingText()
	{
		var markdown = Article.Replace("\"heading\":\"Examples\"", "\"heading\":\"`money()` and **balances**\"")
			.Replace("## Examples", "## `money()` and **balances**");
		var article = HelpArticleParser.Parse(markdown, "help").Single().Article;
		var html = HelpHtmlRenderer.RenderToHtml(article.Markdown, topic => "/help/" + Uri.EscapeDataString(topic), article);
		await Assert.That(html).Contains(">money() and balances</a>");
		await Assert.That(html).Contains("<code>money()</code> and <strong>balances</strong>");
	}

	[Test]
	public async Task LegacyAliasesIgnoreFencedAndIndentedHeadings()
	{
		const string markdown = "# real\n# alias\nBody\n```sharp\n# fenced\n```\n\n    # indented\n\n# next\nNext body";
		var articles = HelpArticleParser.Parse(markdown, "help");
		await Assert.That(articles.Count).IsEqualTo(2);
		await Assert.That(articles[0].Article.Aliases).Contains("alias");
		await Assert.That(articles[0].Article.Overview).Contains("# fenced");
	}

	[Test]
	public async Task TerminalReferencesAreReadableAndCorpusScoped()
	{
		var rendered = RecursiveMarkdownHelper.RenderMarkdown("Related: [object snapshots]", corpus: "ahelp");
		await Assert.That(rendered.ToPlainText()).Contains("ahelp object snapshots");
	}

	[Test]
	[Arguments("Type help [newbie].", "help", "Type help newbie.")]
	[Arguments("Type `help` [newbie].", "help", "Type help newbie.")]
	[Arguments("Type ahelp [object snapshots].", "ahelp", "Type ahelp object snapshots.")]
	[Arguments("Type news [announcements].", "news", "Type news announcements.")]
	[Arguments("Related: [newbie].", "help", "Related: help newbie.")]
	[Arguments("Word somehelp [newbie].", "help", "Word somehelp help newbie.")]
	public async Task TerminalCommandLabelsRespectAnAuthoredPrefix(string markdown, string corpus, string expected)
	{
		await Assert.That(RecursiveMarkdownHelper.RenderMarkdown(markdown, corpus: corpus).ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task ExplicitH3LookupRetainsItsLocalContentOnly()
	{
		var markdown = Article.Replace("\"sections\":[", "\"sections\":[{\"id\":\"narrow\",\"heading\":\"Local explanation\",\"lookup\":\"sample explanation\"},");
		var article = HelpArticleParser.Parse(markdown, "help").Single().Article;
		var section = article.Sections.Single(section => section.Id == "narrow");
		await Assert.That(article.Entry(section).Markdown).Contains("Example details");
		await Assert.That(article.Entry(section).Markdown).DoesNotContain("significant");
		await Assert.That(article.Sections.Single(section => section.Id == "examples").Markdown).DoesNotContain("Local explanation");
	}

	[Test]
	[Arguments("\"id\":\"options\"", "\"id\":\"examples\"")]
	[Arguments("\"heading\":\"Options\"", "\"heading\":\"Missing\"")]
	[Arguments("\"SAMPLE2\":\"sample examples\"", "\"SAMPLE2\":\"SAMPLE2\"")]
	[Arguments("\"corpus\":\"help\"", "\"corpus\":\"ahelp\"")]
	public async Task InvalidDeclarationsAreRejected(string before, string after)
	{
		await Assert.That(() => HelpArticleParser.Parse(Article.Replace(before, after), "help"))
			.Throws<InvalidDataException>();
	}

	[Test]
	public async Task EveryShippedCorpusPassesIntegrityValidation()
	{
		foreach (var directory in Directory.GetDirectories("TextFiles"))
		{
			var corpus = Path.GetFileName(directory);
			var articles = Directory.GetFiles(directory, "*.md")
				.SelectMany(file => HelpArticleParser.Parse(File.ReadAllText(file), corpus)).ToList();
			HelpCorpusValidator.Validate(articles);
			await Assert.That(articles.Count).IsGreaterThan(0);
		}
	}

	[Test]
	[Arguments("[missing topic]")]
	[Arguments("[SAMPLE2]")]
	public async Task MissingAndDeprecatedLinksAreRejected(string link)
	{
		var parsed = HelpArticleParser.Parse(Article + "\n" + link, "help");
		await Assert.That(() => HelpCorpusValidator.Validate(parsed)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task NewNumberedContinuationsAreRejected()
	{
		var parsed = HelpArticleParser.Parse(Article, "help")
			.Concat(HelpArticleParser.Parse("# sample2\nContinuation.", "help")).ToList();
		await Assert.That(() => HelpCorpusValidator.Validate(parsed)).Throws<InvalidDataException>();
	}
}
