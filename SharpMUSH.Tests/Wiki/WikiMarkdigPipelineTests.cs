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
		await Assert.That(html).Contains("href=\"/wiki/main/general/getting_started\"");
		await Assert.That(html).Contains("Getting Started");
	}

	[Test]
	public async Task RenderToHtml_WikiLinkWithDisplayText_UsesDisplayText()
	{
		var html = Pipeline().RenderToHtml("See [[Click here|getting_started]] for details.");

		await Assert.That(html).Contains("href=\"/wiki/main/general/getting_started\"");
		await Assert.That(html).Contains("Click here");
	}

	[Test]
	public async Task RenderToHtml_WikiLinkWithNamespacePrefix_IncludesPrefixInHref()
	{
		// [[Help:Getting Started]] → href="/wiki/help/general/getting_started"
		var html = Pipeline().RenderToHtml("[[Help:Getting Started]]");

		await Assert.That(html).Contains("href=\"/wiki/help/general/getting_started\"");
	}

	[Test]
	public async Task RenderToHtml_WikiLinkWithUnderscoredSlug_SlugsCorrectly()
	{
		var html = Pipeline().RenderToHtml("[[my_page]]");

		await Assert.That(html).Contains("href=\"/wiki/main/general/my_page\"");
	}

	[Test]
	public async Task RenderToHtml_MultipleWikiLinks_AllRendered()
	{
		var html = Pipeline().RenderToHtml("[[Page A]] and [[Page B]].");

		await Assert.That(html).Contains("href=\"/wiki/main/general/page_a\"");
		await Assert.That(html).Contains("href=\"/wiki/main/general/page_b\"");
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

	// ── WikiLinkExtension — named contract tests ───────────────────────────────

	/// <summary>
	/// [[My Page]] → &lt;a href="/wiki/main/general/my_page"&gt;My Page&lt;/a&gt;
	/// </summary>
	[Test]
	public async Task WikiLinkExtension_ValidPage_EmitsAnchorTag()
	{
		var html = Pipeline().RenderToHtml("[[My Page]]");

		await Assert.That(html).Contains("<a href=\"/wiki/main/general/my_page\">");
		await Assert.That(html).Contains("My Page");
	}

	/// <summary>
	/// A <see cref="WikiLinkInline"/> node constructed with <c>IsRedLink = true</c>
	/// must render with <c>class="wiki-redlink"</c>.
	/// The parser always sets IsRedLink=false (deferred until DB integration), so
	/// we exercise the renderer directly using an injected node.
	/// </summary>
	[Test]
	public async Task WikiLinkExtension_MissingPage_EmitsRedlinkClass()
	{
		// Build a minimal Markdig pipeline with the WikiLinkExtension registered.
		var pipeline = WikiMarkdigPipeline.CreatePipeline();

		// Build an AST manually: a document containing a paragraph with one WikiLinkInline.
		var doc = new Markdig.Syntax.MarkdownDocument();
		var para = new Markdig.Syntax.ParagraphBlock();
		var inline = new Markdig.Syntax.Inlines.ContainerInline();
		var node = new WikiLinkInline
		{
			Slug = "missing_page",
			Title = "Missing Page",
			IsRedLink = true,
		};
		inline.AppendChild(node);
		para.Inline = inline;
		doc.Add(para);

		// Render the pre-built AST through Markdig's HTML renderer.
		var writer = new System.IO.StringWriter();
		var renderer = new Markdig.Renderers.HtmlRenderer(writer);
		pipeline.Setup(renderer);
		renderer.Render(doc);
		var html = writer.ToString();

		await Assert.That(html).Contains("wiki-redlink");
		await Assert.That(html).Contains("href=\"/wiki/missing_page\"");
	}

	/// <summary>
	/// [[Help:Topic]] → href="/wiki/help/topic"
	/// </summary>
	[Test]
	public async Task WikiLinkExtension_NamespacedPage_ResolvesCorrectly()
	{
		var html = Pipeline().RenderToHtml("[[Help:Getting Started]]");

		await Assert.That(html).Contains("href=\"/wiki/help/general/getting_started\"");
	}

	/// <summary>
	/// [[Click Here|My Page]] → anchor text is "Click Here", not the slug.
	/// </summary>
	[Test]
	public async Task WikiLinkExtension_DisplayText_UsesDisplayNotSlug()
	{
		var html = Pipeline().RenderToHtml("[[Click Here|my_page]]");

		await Assert.That(html).Contains("href=\"/wiki/main/general/my_page\"");
		await Assert.That(html).Contains("Click Here");
		await Assert.That(html).DoesNotContain("my_page</"); // slug must not appear as link text
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

	// ── Image handling ────────────────────────────────────────────────────────

	/// <summary>
	/// Standard Markdown image syntax should produce an &lt;img&gt; with
	/// <c>loading="lazy"</c> and <c>class="wiki-img"</c> for CSS-driven lightbox.
	/// </summary>
	[Test]
	public async Task RenderToHtml_Image_EmitsLazyLoadAndWikiImgClass()
	{
		var html = Pipeline().RenderToHtml("![A cat sitting](https://example.com/cat.jpg)");

		await Assert.That(html).Contains("src=\"https://example.com/cat.jpg\"");
		await Assert.That(html).Contains("alt=\"A cat sitting\"");
		await Assert.That(html).Contains("loading=\"lazy\"");
		await Assert.That(html).Contains("class=\"wiki-img\"");
	}

	/// <summary>
	/// Image with empty alt text still renders with lazy loading and wiki-img class.
	/// </summary>
	[Test]
	public async Task RenderToHtml_ImageNoAlt_EmitsLazyLoadAndWikiImgClass()
	{
		var html = Pipeline().RenderToHtml("![](https://example.com/bg.png)");

		await Assert.That(html).Contains("src=\"https://example.com/bg.png\"");
		await Assert.That(html).Contains("loading=\"lazy\"");
		await Assert.That(html).Contains("class=\"wiki-img\"");
	}

	// ── Image sizing via generic attributes ───────────────────────────────────

	/// <summary>
	/// <c>![alt](src){width=200 height=100}</c> emits width/height on the img tag.
	/// </summary>
	[Test]
	public async Task RenderToHtml_ImageWithWidthHeight_EmitsDimensionAttributes()
	{
		var html = Pipeline().RenderToHtml("![SharpMUSH logo](/assets/Logo.svg){width=200 height=100}");

		await Assert.That(html).Contains("width=\"200\"");
		await Assert.That(html).Contains("height=\"100\"");
		await Assert.That(html).Contains("class=\"wiki-img\"");
		await Assert.That(html).Contains("loading=\"lazy\"");
	}

	/// <summary>Percentage dimensions are accepted.</summary>
	[Test]
	public async Task RenderToHtml_ImageWithPercentWidth_EmitsWidth()
	{
		var html = Pipeline().RenderToHtml("![banner](/img/banner.png){width=50%}");

		await Assert.That(html).Contains("width=\"50%\"");
	}

	/// <summary>
	/// Non-numeric dimension values are dropped — they could otherwise smuggle
	/// markup into the attribute position.
	/// </summary>
	[Test]
	public async Task RenderToHtml_ImageWithInvalidWidth_DropsAttribute()
	{
		var html = Pipeline().RenderToHtml("![x](/img/x.png){width=\"200px\" height=javascript}");

		await Assert.That(html).DoesNotContain("width=");
		await Assert.That(html).DoesNotContain("height=");
	}

	/// <summary>
	/// Dangerous generic attributes (onerror, style, id) are never emitted —
	/// only the whitelisted width/height/class subset survives.
	/// </summary>
	[Test]
	public async Task RenderToHtml_ImageWithEventHandlerAttribute_DropsIt()
	{
		var html = Pipeline().RenderToHtml("![x](/img/x.png){onerror=alert(1) style=position:fixed width=64}");

		await Assert.That(html).DoesNotContain("onerror");
		await Assert.That(html).DoesNotContain("style=");
		await Assert.That(html).Contains("width=\"64\"");
	}

	/// <summary>
	/// Author-supplied CSS classes ({.logo}) are appended after wiki-img.
	/// </summary>
	[Test]
	public async Task RenderToHtml_ImageWithCssClass_AppendsToWikiImgClass()
	{
		var html = Pipeline().RenderToHtml("![logo](/assets/Logo.svg){.logo width=200}");

		await Assert.That(html).Contains("class=\"wiki-img logo\"");
	}

	/// <summary>Class names outside the safe identifier charset are dropped.</summary>
	[Test]
	public async Task RenderToHtml_ImageWithUnsafeCssClass_DropsIt()
	{
		var html = Pipeline().RenderToHtml("![x](/img/x.png){.bad\"onload=alert(1)}");

		await Assert.That(html).Contains("class=\"wiki-img\"");
		await Assert.That(html).DoesNotContain("onload");
	}

	// ── Wiki directive containers (WikiDirectiveExtension) ───────────────────

	/// <summary>::: category lore → placeholder div with data attributes.</summary>
	[Test]
	public async Task RenderToHtml_CategoryDirective_EmitsPlaceholderDiv()
	{
		var html = Pipeline().RenderToHtml("::: category lore\n:::");

		await Assert.That(html)
			.Contains("<div class=\"wiki-directive\" data-directive=\"category\" data-arg=\"lore\"></div>");
	}

	/// <summary>::: tag magic → placeholder div with data attributes.</summary>
	[Test]
	public async Task RenderToHtml_TagDirective_EmitsPlaceholderDiv()
	{
		var html = Pipeline().RenderToHtml("::: tag magic\n:::");

		await Assert.That(html)
			.Contains("<div class=\"wiki-directive\" data-directive=\"tag\" data-arg=\"magic\"></div>");
	}

	/// <summary>::: pagelist help → placeholder div with data attributes.</summary>
	[Test]
	public async Task RenderToHtml_PagelistDirective_EmitsPlaceholderDiv()
	{
		var html = Pipeline().RenderToHtml("::: pagelist help\n:::");

		await Assert.That(html)
			.Contains("<div class=\"wiki-directive\" data-directive=\"pagelist\" data-arg=\"help\"></div>");
	}

	/// <summary>::: recent 10 → placeholder div with the count as the arg.</summary>
	[Test]
	public async Task RenderToHtml_RecentDirective_EmitsPlaceholderDiv()
	{
		var html = Pipeline().RenderToHtml("::: recent 10\n:::");

		await Assert.That(html)
			.Contains("<div class=\"wiki-directive\" data-directive=\"recent\" data-arg=\"10\"></div>");
	}

	/// <summary>The recent count is clamped to 50 when the author asks for more.</summary>
	[Test]
	public async Task RenderToHtml_RecentDirectiveOverMax_ClampsTo50()
	{
		var html = Pipeline().RenderToHtml("::: recent 999\n:::");

		await Assert.That(html).Contains("data-arg=\"50\"");
	}

	/// <summary>A non-numeric recent argument renders no directive at all.</summary>
	[Test]
	public async Task RenderToHtml_RecentDirectiveNonNumeric_RendersNothing()
	{
		var html = Pipeline().RenderToHtml("::: recent lots\n:::");

		await Assert.That(html).DoesNotContain("wiki-directive");
	}

	/// <summary>
	/// An argument outside the safe charset (e.g. attempted markup injection)
	/// renders no directive div — the container is dropped entirely.
	/// </summary>
	[Test]
	public async Task RenderToHtml_DirectiveWithInvalidArg_RendersNoDirectiveDiv()
	{
		var html = Pipeline().RenderToHtml("::: category <script>alert(1)</script>\n:::");

		await Assert.That(html).DoesNotContain("wiki-directive");
		await Assert.That(html).DoesNotContain("<script>");
	}

	/// <summary>
	/// Custom containers with an unknown info string keep the default Markdig
	/// custom-container rendering (div with the info as its class, children kept).
	/// </summary>
	[Test]
	public async Task RenderToHtml_UnknownContainerInfo_KeepsDefaultRendering()
	{
		var html = Pipeline().RenderToHtml("::: warning\nBe careful!\n:::");

		await Assert.That(html).Contains("<div class=\"warning\">");
		await Assert.That(html).Contains("Be careful!");
		await Assert.That(html).DoesNotContain("wiki-directive");
	}

	/// <summary>
	/// Directive placeholders contribute no text to plain-text extraction —
	/// surrounding prose survives, the directive itself disappears.
	/// </summary>
	[Test]
	public async Task ExtractPlainText_DirectivePage_ContributesNoDirectiveText()
	{
		var text = Pipeline().ExtractPlainText("Before text.\n\n::: category lore\n:::\n\nAfter text.");

		await Assert.That(text).Contains("Before text.");
		await Assert.That(text).Contains("After text.");
		await Assert.That(text).DoesNotContain("category");
		await Assert.That(text).DoesNotContain("lore");
	}

	// ── Mermaid diagrams (Markdig Diagrams extension) ────────────────────────

	/// <summary>
	/// A ```mermaid fenced block is emitted as a &lt;pre class="mermaid"&gt; (Markdig's
	/// Diagrams extension, active via UseAdvancedExtensions) rather than a code block,
	/// so the client-side mermaid.js renderer (which targets the .mermaid class) can turn
	/// it into an SVG. The diagram source survives un-escaped inside for the JS pass.
	/// </summary>
	[Test]
	public async Task RenderToHtml_MermaidFence_EmitsMermaidContainer()
	{
		var html = Pipeline().RenderToHtml("```mermaid\nflowchart LR\nA-->B\n```");

		await Assert.That(html).Contains("class=\"mermaid\"");
		await Assert.That(html).Contains("flowchart LR");
		await Assert.That(html).Contains("A-->B"); // arrow not HTML-escaped — mermaid reads textContent
		await Assert.That(html).DoesNotContain("<pre><code");
	}

	/// <summary>A non-mermaid fenced block stays a normal code block.</summary>
	[Test]
	public async Task RenderToHtml_PlainCodeFence_StaysCodeBlock()
	{
		var html = Pipeline().RenderToHtml("```\n@emit hi\n```");

		await Assert.That(html).Contains("<pre><code");
		await Assert.That(html).DoesNotContain("class=\"mermaid\"");
	}
}
