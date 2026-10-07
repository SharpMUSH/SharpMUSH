using Markdig.Syntax;
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

	[Test]
	public async Task WebKeepsColumnsTheTerminalLinesUp()
	{
		const string markdown = "ACTION LISTS     ANCESTORS<br>\nCHAT             CLIENTS";
		var html = HelpHtmlRenderer.RenderToHtml(markdown, topic => "/help/" + topic);
		await Assert.That(html).IsEqualTo("<p class=\"help-aligned\">ACTION LISTS     ANCESTORS<br>CHAT             CLIENTS</p>\n");
		await Assert.That(RecursiveMarkdownHelper.RenderMarkdown(markdown).ToPlainText())
			.IsEqualTo("ACTION LISTS     ANCESTORS\nCHAT             CLIENTS");
	}

	[Test]
	[Arguments("One line\nwraps here.", "<p>One line wraps here.</p>\n")]
	[Arguments("Hard break  \nnext line.", "<p>Hard break<br />next line.</p>\n")]
	[Arguments("Two spaces.  Then prose.", "<p>Two spaces.  Then prose.</p>\n")]
	public async Task WebLineBreaksAreWhatTheTerminalPrints(string markdown, string expected)
	{
		await Assert.That(HelpHtmlRenderer.RenderToHtml(markdown, topic => "/help/" + topic)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("Commands:<br>\n    @set me=x<br>\n    &attr me=y",
		"<p>Commands:<br><span class=\"help-indent\">    </span>@set me=x<br><span class=\"help-indent\">    </span>&amp;attr me=y</p>\n")]
	[Arguments("  Example:<br>\n    > think [MATCHING]",
		"<p><span class=\"help-indent\">  </span>Example:<br><span class=\"help-indent\">    </span>&gt; think <a href=\"/help/MATCHING\">MATCHING</a></p>\n")]
	[Arguments("Hard break  \n  indented.", "<p>Hard break<br /><span class=\"help-indent\">  </span>indented.</p>\n")]
	[Arguments("1. Either:<br>\n     a. this", "<ol>\n<li>Either:<br><span class=\"help-indent\">  </span>a. this</li>\n</ol>\n")]
	[Arguments("Prose that\n    wraps.", "<p>Prose that wraps.</p>\n")]
	public async Task WebKeepsTheIndentationOfALineAfterABreak(string markdown, string expected)
	{
		await Assert.That(HelpHtmlRenderer.RenderToHtml(markdown, topic => "/help/" + topic)).IsEqualTo(expected);
	}

	/// <summary>
	/// A topic shows one title. Stacked headings name one topic several ways: the first H1 of a stack is
	/// the title and the rest are its aliases, which the reader never sees as headings. A heading with
	/// nothing under it but one other heading (<c>## Trigger examples</c>, then <c>### Examples</c>)
	/// shows two headings for one thing. A section opening straight into several subsections is fine.
	/// </summary>
	[Test]
	public async Task EveryTopicShowsOneTitleAndNoRedundantHeadings()
	{
		var offending = new List<string>();
		foreach (var file in TestPaths.Helpfiles.EnumerateFiles("*.md", SearchOption.AllDirectories))
		{
			var corpus = file.Directory!.Name is "ahelp" or "news" ? file.Directory.Name : "help";
			foreach (var parsed in HelpArticleParser.Parse(File.ReadAllText(file.FullName), corpus))
			{
				var markdown = parsed.Article.Markdown;
				var blocks = Markdig.Markdown.Parse(markdown).ToList();
				if (blocks.OfType<HeadingBlock>().Count(heading => heading.Level == 1) != 1)
				{
					offending.Add($"{file.Name}: {parsed.Article.Lookup} shows more than one title");
				}
				for (var i = 0; i + 1 < blocks.Count; i++)
				{
					if (blocks[i] is not HeadingBlock { Level: > 1 } heading || blocks[i + 1] is not HeadingBlock next)
					{
						continue;
					}
					var children = blocks.Skip(i + 1).OfType<HeadingBlock>()
						.TakeWhile(below => below.Level > heading.Level)
						.Count(below => below.Level == heading.Level + 1);
					if (next.Level <= heading.Level || children == 1)
					{
						offending.Add($"{file.Name}: {parsed.Article.Lookup} stacks {HelpArticleParser.HeadingText(markdown, heading)}"
							+ $" on {HelpArticleParser.HeadingText(markdown, next)}");
					}
				}
			}
		}

		await Assert.That(offending).IsEmpty();
	}

	[Test]
	public async Task TerminalTableCellsThatWrapStayInTheirColumn()
	{
		const string markdown = "| Failure to... | Lock |\n| --- | --- |\n| run an `$-command` on an object that is quite a long way from here | Command |";
		var lines = RecursiveMarkdownHelper.RenderMarkdown(markdown, maxWidth: 40).ToPlainText().Split('\n');
		var divider = lines[0].IndexOf('|');
		await Assert.That(divider).IsGreaterThan(0);
		foreach (var line in lines.Where((_, i) => i != 1))
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(40);
			await Assert.That(line.IndexOf('|')).IsEqualTo(divider);
		}
	}

	private const string TopicList = "Topics:\n\n|     |     |\n|-----|-----|\n| [newbie] | [ZONES] |\n\nAfter.";

	[Test]
	public async Task TerminalTopicListsNameTheTopicsBare()
	{
		await Assert.That(RecursiveMarkdownHelper.RenderMarkdown(TopicList).ToPlainText())
			.IsEqualTo("Topics:\n\nnewbie  ZONES\n\nAfter.");
	}

	[Test]
	public async Task WebTopicListsDropTheEmptyHeader()
	{
		var html = HelpHtmlRenderer.RenderToHtml(TopicList, topic => "/help/" + topic);
		await Assert.That(html).Contains("<table class=\"help-list\">");
		await Assert.That(html).DoesNotContain("<thead>");
		await Assert.That(html).Contains("<a href=\"/help/newbie\">newbie</a>");
	}

	private const string SeeAlso = "Body.\n\n::: seealso\n- [newbie]\n- [ZONES]\n- `[NO_TEL]`\n:::";

	[Test]
	public async Task TerminalSeeAlsoIsOneCommaSeparatedLine()
	{
		await Assert.That(RecursiveMarkdownHelper.RenderMarkdown(SeeAlso).ToPlainText())
			.IsEqualTo("Body.\n\nSee Also: newbie, ZONES, [NO_TEL]");
	}

	[Test]
	public async Task TerminalSeeAlsoWrapsAtACommaUnderTheFirstTopic()
	{
		var lines = RecursiveMarkdownHelper.RenderMarkdown(SeeAlso, maxWidth: 24).ToPlainText().Split('\n');
		await Assert.That(lines[^2]).IsEqualTo("See Also: newbie, ZONES,");
		await Assert.That(lines[^1]).IsEqualTo("          [NO_TEL]");
	}

	[Test]
	public async Task SeeAlsoWithDescribedItemsStaysAListUnderTheLabel()
	{
		const string markdown = "::: seealso\n- [newbie] — where to start\n:::";
		await Assert.That(RecursiveMarkdownHelper.RenderMarkdown(markdown).ToPlainText())
			.IsEqualTo("See Also:\n* help newbie — where to start");
		var html = HelpHtmlRenderer.RenderToHtml(markdown, topic => "/help/" + topic);
		await Assert.That(html).Contains("<span class=\"help-see-also-label\">See Also</span>");
		await Assert.That(html).Contains("<li><a href=\"/help/newbie\">newbie</a> — where to start</li>");
		await Assert.That(html).DoesNotContain("help-see-also-names");
	}

	[Test]
	public async Task BoldSeeAlsoLabelIsOnlyAParagraph()
	{
		const string markdown = "**See Also:**\n- [newbie]";
		await Assert.That(RecursiveMarkdownHelper.RenderMarkdown(markdown).ToPlainText()).IsEqualTo("See Also:\n* help newbie");
	}

	[Test]
	public async Task WebSeeAlsoIsALabelledNavOfLinks()
	{
		var html = HelpHtmlRenderer.RenderToHtml(SeeAlso, topic => "/help/" + topic);
		await Assert.That(html).IsEqualTo("<p>Body.</p>\n<nav class=\"help-see-also\" aria-label=\"See also\"><span class=\"help-see-also-label\">See Also</span>"
			+ "<ul class=\"help-see-also-names\"><li><a href=\"/help/newbie\">newbie</a></li><li><a href=\"/help/ZONES\">ZONES</a></li><li><code>[NO_TEL]</code></li></ul></nav>\n");
	}
}
