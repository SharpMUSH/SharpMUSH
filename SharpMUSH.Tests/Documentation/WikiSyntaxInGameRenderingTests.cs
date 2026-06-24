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
		var rendered = RecursiveMarkdownHelper.RenderMarkdown("[[Getting Started]]").ToString();

		await Assert.That(rendered).Contains("[4m");
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
}
