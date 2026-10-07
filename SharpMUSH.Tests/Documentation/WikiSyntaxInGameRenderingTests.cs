using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Verifies that the wiki's Markdown extensions degrade gracefully when rendered
/// to a terminal via <see cref="RecursiveMarkdownHelper"/> — the path used by the
/// in-game <c>help</c>/<c>news</c> commands and the <c>rendermarkdown()</c> softcode
/// function. None of the web-only syntax may leak as literal markup.
/// </summary>
public class WikiSyntaxInGameRenderingTests
{
	private static string Render(string markdown) =>
		RecursiveMarkdownHelper.RenderMarkdown(markdown).ToPlainText();

	/// <summary>A <c>::: center</c> block centres each line in the width, with no fill after it.</summary>
	[Test]
	public async Task CenterContainer_CentresEachLineInTheWidth()
	{
		var lines = RecursiveMarkdownHelper.RenderMarkdown("::: center\n# Welcome\n\nOne line.\n:::", maxWidth: 20)
			.ToPlainText()
			.Split('\n');

		await Assert.That(lines).Contains("      Welcome");
		await Assert.That(lines).Contains("     One line.");
		await Assert.That(lines.Any(line => line.EndsWith(' '))).IsFalse();
	}

	/// <summary>A line wider than the width wraps at a space, and each piece is centred.</summary>
	[Test]
	public async Task CenterContainer_WrapsALongLineThenCentresEachPiece()
	{
		var lines = RecursiveMarkdownHelper.RenderMarkdown("::: center\nalpha beta gamma delta\n:::", maxWidth: 12)
			.ToPlainText()
			.Split('\n');

		await Assert.That(lines).IsEquivalentTo([" alpha beta", "gamma delta"]);
	}

	/// <summary>Text outside a <c>::: center</c> block keeps its left edge.</summary>
	[Test]
	public async Task TextOutsideCenterContainer_StaysLeftAligned()
	{
		var text = RecursiveMarkdownHelper.RenderMarkdown("Left.\n\n::: center\nMiddle\n:::", maxWidth: 20).ToPlainText();

		await Assert.That(text).StartsWith("Left.");
		await Assert.That(text).Contains("       Middle");
	}

	[Test]
	public async Task ImageWithSizeAttributes_AttributeBlockDoesNotLeak()
	{
		var text = Render("![SharpMUSH logo](/assets/Logo.svg){width=200 height=100}");

		await Assert.That(text).Contains("[image: SharpMUSH logo]");
		await Assert.That(text).DoesNotContain("{width");
		await Assert.That(text).DoesNotContain("height=100");
	}

	[Test]
	public async Task ImageWithPercentWidth_AttributeBlockDoesNotLeak()
	{
		var text = Render("![logo](/assets/Logo.svg){width=20%}");

		await Assert.That(text).Contains("[image: logo]");
		await Assert.That(text).DoesNotContain("20%");
	}

	/// <summary>
	/// A surface that may show pictures (<c>@wiki</c>, or markdown rendered for an object with
	/// Send_OOB) wraps the placeholder in the picture: a client that shows pictures draws it, and a
	/// terminal still reads the placeholder.
	/// </summary>
	[Test]
	public async Task ImageAllowed_ShowsThePictureAndKeepsThePlaceholderForATerminal()
	{
		var rendered = RecursiveMarkdownHelper.RenderMarkdown(
			"![SharpMUSH logo](/assets/Logo.svg){width=200px height=100}",
			new RecursiveMarkdownRenderer { ImageAllowed = _ => true });

		var html = rendered.Render(MarkupFormat.Html);
		await Assert.That(html).Contains("src=\"/assets/Logo.svg\"");
		await Assert.That(html).Contains("alt=\"SharpMUSH logo\"");
		await Assert.That(html).Contains("width=\"200\"");
		await Assert.That(html).Contains("height=\"100\"");
		await Assert.That(html).DoesNotContain("[image:");
		await Assert.That(rendered.Render(MarkupFormat.Mxp)).Contains("<IMAGE");
		await Assert.That(rendered.Render(MarkupFormat.Ansi)).Contains("[image: SharpMUSH logo]");
		await Assert.That(rendered.ToPlainText()).Contains("[image: SharpMUSH logo]");
	}

	/// <summary>
	/// An image alone in its paragraph is a figure, as <c>figure()</c> draws one; an image in a sentence
	/// stays in the line.
	/// </summary>
	[Test]
	public async Task ImageAllowed_AloneIsAFigureAndInASentenceStaysInline()
	{
		var renderer = () => new RecursiveMarkdownRenderer { ImageAllowed = _ => true };
		var alone = RecursiveMarkdownHelper.RenderMarkdown("![A map](/m.png)", renderer());
		var inline = RecursiveMarkdownHelper.RenderMarkdown("See ![A map](/m.png) here.", renderer());

		await Assert.That(alone.Render(MarkupFormat.Html)).Contains("class=\"ms-figure-image\"");
		await Assert.That(alone.Render(MarkupFormat.Mxp)).Contains("<IMAGE /m.png");
		await Assert.That(inline.Render(MarkupFormat.Html)).Contains("See <img class=\"ms-image\" src=\"/m.png\"");
		await Assert.That(inline.ToPlainText()).IsEqualTo("See [image: A map] here.");
	}

	/// <summary>A lone image inside <c>::: center</c> is centred for a terminal too, like the text around it.</summary>
	[Test]
	public async Task ImageAllowed_AloneInACentredBlockIsCentred()
	{
		var lines = RecursiveMarkdownHelper.RenderMarkdown("::: center\n![logo](/l.svg)\n:::",
			new RecursiveMarkdownRenderer(20) { ImageAllowed = _ => true }).ToPlainText().Split('\n');

		await Assert.That(lines[0].TrimEnd()).IsEqualTo("   [image: logo]");
	}

	/// <summary>A percentage has no pixel count, so the picture is left its own size.</summary>
	[Test]
	public async Task ImageAllowed_PercentWidthIsNotAPixelWidth()
	{
		var html = RecursiveMarkdownHelper.RenderMarkdown(
			"![logo](/assets/Logo.svg){width=20%}",
			new RecursiveMarkdownRenderer { ImageAllowed = _ => true }).Render(MarkupFormat.Html);

		await Assert.That(html).Contains("src=\"/assets/Logo.svg\"");
		await Assert.That(html).DoesNotContain("width=");
	}

	/// <summary>An address the policy refuses, and every image when there is no policy, stays the placeholder.</summary>
	[Test]
	public async Task ImageRefusedOrNoPolicy_IsOnlyThePlaceholder()
	{
		const string markdown = "![logo](https://elsewhere.example/logo.png)";
		var refused = RecursiveMarkdownHelper.RenderMarkdown(markdown,
			new RecursiveMarkdownRenderer { ImageAllowed = source => source.StartsWith('/') });
		var unset = RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(refused.Render(MarkupFormat.Html)).DoesNotContain("<img");
		await Assert.That(unset.Render(MarkupFormat.Html)).DoesNotContain("<img");
		await Assert.That(refused.ToPlainText()).Contains("[image: logo]");
	}

	[Test]
	public async Task HeadingWithAttributeBlock_DoesNotLeak()
	{
		var text = Render("# Title {.fancy}");

		await Assert.That(text).Contains("Title");
		await Assert.That(text).DoesNotContain("{.fancy}");
	}

	[Test]
	public async Task WikiLink_RendersDisplayTitleWithoutBrackets()
	{
		var text = Render("See [[Getting Started]] for details.");

		await Assert.That(text).Contains("Getting Started");
		await Assert.That(text).DoesNotContain("[[");
		await Assert.That(text).DoesNotContain("]]");
	}

	[Test]
	public async Task WikiLinkWithDisplayText_UsesDisplayText()
	{
		var text = Render("[[Click here|getting_started]]");

		await Assert.That(text).Contains("Click here");
		await Assert.That(text).DoesNotContain("getting_started");
	}

	[Test]
	public async Task NamespacedWikiLink_RendersTitle()
	{
		var text = Render("[[Help:Markdown Guide]]");

		await Assert.That(text).Contains("Markdown Guide");
		await Assert.That(text).DoesNotContain("Help:");
	}

	[Test]
	public async Task WikiLink_IsUnderlinedInAnsiOutput()
	{
		var rendered = RecursiveMarkdownHelper.RenderMarkdown("[[Getting Started]]").Render(MarkupFormat.Ansi);

		await Assert.That(AnsiStream.Sets(rendered, 4)).IsTrue();
	}

	[Test]
	public async Task CategoryDirective_RendersPlaceholderNotFences()
	{
		var text = Render("Intro.\n\n::: category lore\n:::\n\nOutro.");

		await Assert.That(text).Contains("[live listing: category lore");
		await Assert.That(text).DoesNotContain(":::");
		await Assert.That(text).Contains("Intro.");
		await Assert.That(text).Contains("Outro.");
	}

	[Test]
	public async Task RecentDirective_RendersPlaceholder()
	{
		var text = Render("::: recent 5\n:::");

		await Assert.That(text).Contains("[live listing: recent 5");
	}

	[Test]
	public async Task TagAndPagelistDirectives_RenderPlaceholders()
	{
		var text = Render("::: tag magic\n:::\n\n::: pagelist help\n:::");

		await Assert.That(text).Contains("[live listing: tag magic");
		await Assert.That(text).Contains("[live listing: pagelist help");
	}

	[Test]
	public async Task UnknownCustomContainer_RendersItsContent()
	{
		var text = Render("::: warning\nDanger ahead.\n:::");

		await Assert.That(text).Contains("Danger ahead.");
		await Assert.That(text).DoesNotContain(":::");
		await Assert.That(text).DoesNotContain("live listing");
	}

	[Test]
	public async Task DirectiveExampleInsideCodeFence_StaysLiteral()
	{
		var text = Render("```\n::: category lore\n:::\n```");

		// Inside a code fence the syntax is documentation, not a directive.
		await Assert.That(text).Contains("::: category lore");
		await Assert.That(text).DoesNotContain("live listing");
	}

	[Test]
	public async Task TaskList_RendersCheckboxNotation()
	{
		var text = Render("- [x] Done item\n- [ ] Open item");

		await Assert.That(text).Contains("[x]");
		await Assert.That(text).Contains("Done item");
		await Assert.That(text).Contains("[ ]");
		await Assert.That(text).Contains("Open item");
	}

	/// <summary>
	/// The helpfiles that <em>document</em> <c>[[wiki link]]</c> syntax are themselves rendered by this
	/// renderer, which eats <c>[[...]]</c>. Every occurrence in the prose is inside a code span for that
	/// reason; this pins it, because losing the brackets would leave help text explaining a syntax it
	/// cannot show.
	/// </summary>
	[Test]
	[Arguments("wiki.md", "[[Page Name]]")]
	[Arguments("render-markdown-custom.md", "[[Page Name]]")]
	public async Task Helpfile_DocumentingWikiLinkSyntax_KeepsItsBrackets(string file, string expected)
	{
		var helpDir = FindHelpfilesDirectory();
		if (helpDir is null) return; // running outside the repo

		var text = Render(await File.ReadAllTextAsync(Path.Join(helpDir, file)));

		await Assert.That(text).Contains(expected);
	}

	private static string? FindHelpfilesDirectory()
	{
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
		while (dir is not null)
		{
			var candidate = Path.Join(dir.FullName, "SharpMUSH.Documentation", "Helpfiles", "SharpMUSH");
			if (Directory.Exists(candidate)) return candidate;
			dir = dir.Parent;
		}
		return null;
	}
}
