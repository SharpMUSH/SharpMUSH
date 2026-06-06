using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for <see cref="WikiMarkdigPipeline"/>.
/// Covers HTML rendering, wiki-link syntax, plain-text extraction, and security (DisableHtml).
/// </summary>
public class WikiMarkdigPipelineTests
{
	// ── Helpers ──────────────────────────────────────────────────────────────

	private static WikiMarkdigPipeline Pipeline() => new();

	// ── RenderToHtml — basic Markdown ─────────────────────────────────────────

	[Test]
	public async Task RenderToHtml_Bold_ProducesStrongTag()
	{
		var html = Pipeline().RenderToHtml("Hello **world**.");

		await Assert.That(html).Contains("<strong>world</strong>");
	}

	[Test]
	public async Task RenderToHtml_Italic_ProducesEmTag()
	{
		var html = Pipeline().RenderToHtml("Hello _world_.");

		await Assert.That(html).Contains("<em>world</em>");
	}

	[Test]
	public async Task RenderToHtml_Heading_ProducesH1Tag()
	{
		var html = Pipeline().RenderToHtml("# Title");

		await Assert.That(html).Contains("<h1");
		await Assert.That(html).Contains("Title");
	}

	[Test]
	public async Task RenderToHtml_UnorderedList_ProducesUlAndLiTags()
	{
		var html = Pipeline().RenderToHtml("- item one\n- item two");

		await Assert.That(html).Contains("<ul>");
		await Assert.That(html).Contains("<li>");
		await Assert.That(html).Contains("item one");
	}

	[Test]
	public async Task RenderToHtml_InlineCode_ProducesCodeTag()
	{
		var html = Pipeline().RenderToHtml("Use `foo()` here.");

		await Assert.That(html).Contains("<code>foo()</code>");
	}

	// ── RenderToHtml — wiki-link syntax ──────────────────────────────────────

	[Test]
	public async Task RenderToHtml_SimpleWikiLink_ProducesAnchorWithSlugHref()
	{
		var html = Pipeline().RenderToHtml("See [[Getting Started]] for details.");

		// Slug derived: "getting_started"
		await Assert.That(html).Contains("href=\"/wiki/getting_started\"");
		await Assert.That(html).Contains("Getting Started");
	}

	[Test]
	public async Task RenderToHtml_WikiLinkWithDisplayText_UsesDisplayText()
	{
		var html = Pipeline().RenderToHtml("See [[Click here|getting_started]] for details.");

		await Assert.That(html).Contains("href=\"/wiki/getting_started\"");
		await Assert.That(html).Contains("Click here");
	}

	[Test]
	public async Task RenderToHtml_WikiLinkWithNamespacePrefix_IncludesPrefixInHref()
	{
		// [[Help:Getting Started]] → href="/wiki/help/getting_started"
		var html = Pipeline().RenderToHtml("[[Help:Getting Started]]");

		await Assert.That(html).Contains("href=\"/wiki/help/getting_started\"");
	}

	[Test]
	public async Task RenderToHtml_WikiLinkWithUnderscoredSlug_SlugsCorrectly()
	{
		var html = Pipeline().RenderToHtml("[[my_page]]");

		await Assert.That(html).Contains("href=\"/wiki/my_page\"");
	}

	[Test]
	public async Task RenderToHtml_MultipleWikiLinks_AllRendered()
	{
		var html = Pipeline().RenderToHtml("[[Page A]] and [[Page B]].");

		await Assert.That(html).Contains("href=\"/wiki/page_a\"");
		await Assert.That(html).Contains("href=\"/wiki/page_b\"");
	}

	// ── RenderToHtml — security (DisableHtml) ────────────────────────────────

	[Test]
	public async Task RenderToHtml_RawHtmlTag_IsEscapedNotPassedThrough()
	{
		var html = Pipeline().RenderToHtml("<script>alert('xss')</script>");

		// DisableHtml strips or escapes raw tags — should not appear as live HTML
		await Assert.That(html).DoesNotContain("<script>");
	}

	[Test]
	public async Task RenderToHtml_HtmlCommentInSource_IsNotRendered()
	{
		var html = Pipeline().RenderToHtml("<!-- hidden --> visible");

		await Assert.That(html).DoesNotContain("<!--");
		await Assert.That(html).Contains("visible");
	}

	[Test]
	public async Task RenderToHtml_InlineHtmlAttribute_IsEscaped()
	{
		var html = Pipeline().RenderToHtml("<b onclick=\"evil()\">text</b>");

		// DisableHtml escapes the raw tag to &lt;b onclick=...&gt; rather than
		// passing it through as live HTML. Verify the unescaped tag is absent.
		await Assert.That(html).DoesNotContain("<b onclick=");
	}

	// ── RenderToHtml — null guard ─────────────────────────────────────────────

	[Test]
	public async Task RenderToHtml_NullInput_ThrowsArgumentNullException()
	{
		var pipe = Pipeline();

		await Assert.That(() => pipe.RenderToHtml(null!))
			.Throws<ArgumentNullException>();
	}

	// ── ExtractPlainText ──────────────────────────────────────────────────────

	[Test]
	public async Task ExtractPlainText_Bold_StripsMdSyntax()
	{
		var text = Pipeline().ExtractPlainText("Hello **world**.");

		await Assert.That(text).Contains("world");
		await Assert.That(text).DoesNotContain("**");
	}

	[Test]
	public async Task ExtractPlainText_Heading_StripsHashSymbol()
	{
		var text = Pipeline().ExtractPlainText("# My Heading");

		await Assert.That(text).Contains("My Heading");
		await Assert.That(text).DoesNotContain("#");
	}

	[Test]
	public async Task ExtractPlainText_WikiLink_PreservesAnchorText()
	{
		var text = Pipeline().ExtractPlainText("See [[Getting Started]] for info.");

		await Assert.That(text).Contains("Getting Started");
	}

	[Test]
	public async Task ExtractPlainText_WikiLinkWithDisplayText_PreservesDisplayText()
	{
		var text = Pipeline().ExtractPlainText("[[Click here|getting_started]]");

		await Assert.That(text).Contains("Click here");
	}

	[Test]
	public async Task ExtractPlainText_NullInput_ThrowsArgumentNullException()
	{
		var pipe = Pipeline();

		await Assert.That(() => pipe.ExtractPlainText(null!))
			.Throws<ArgumentNullException>();
	}

	[Test]
	public async Task ExtractPlainText_MultilineMixed_ReturnsReadableText()
	{
		var md = "# Title\n\nParagraph with **bold** and [[Link]].\n\n- Item one\n- Item two";
		var text = Pipeline().ExtractPlainText(md);

		await Assert.That(text).Contains("Title");
		await Assert.That(text).Contains("bold");
		await Assert.That(text).Contains("Link");
		await Assert.That(text).Contains("Item one");
		await Assert.That(text).DoesNotContain("**");
		await Assert.That(text).DoesNotContain("#");
	}

	// ── Thread safety ─────────────────────────────────────────────────────────

	[Test]
	public async Task RenderToHtml_ConcurrentCalls_AllReturnValidHtml()
	{
		var pipe = Pipeline();
		var tasks = Enumerable.Range(0, 20)
			.Select(i => Task.Run(() => pipe.RenderToHtml($"Page [[Item {i}]] content.")))
			.ToArray();

		var results = await Task.WhenAll(tasks);

		foreach (var html in results)
			await Assert.That(html).Contains("<a href=");
	}
}
