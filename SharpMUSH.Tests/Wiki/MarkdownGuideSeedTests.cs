using SharpMUSH.Library.Services;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Renders the seeded Help:Markdown Guide content through the real wiki pipeline
/// to guarantee the shipped documentation page actually exercises the features it
/// documents (tables, directives, attribute blocks) without rendering errors.
/// </summary>
public class MarkdownGuideSeedTests
{
	[Test]
	public async Task Guide_RendersWithoutError_AndContainsCoreSections()
	{
		var html = new WikiMarkdigPipeline().RenderToHtml(StartupHandler.MarkdownGuideContent);

		await Assert.That(html).Contains("Basic formatting");
		await Assert.That(html).Contains("<table>");
		await Assert.That(html).Contains("<strong>bold</strong>");
	}

	[Test]
	public async Task Guide_LiveRecentDirective_EmitsPlaceholder()
	{
		var html = new WikiMarkdigPipeline().RenderToHtml(StartupHandler.MarkdownGuideContent);

		// The "recent 5" example block must become a live directive placeholder…
		await Assert.That(html).Contains("data-directive=\"recent\" data-arg=\"5\"");
	}

	[Test]
	public async Task Guide_DirectiveExamplesInCodeFences_StayLiteral()
	{
		var html = new WikiMarkdigPipeline().RenderToHtml(StartupHandler.MarkdownGuideContent);

		// …while the fenced syntax examples stay literal text, not live directives.
		await Assert.That(html).DoesNotContain("data-arg=\"lore\"");
		await Assert.That(html).DoesNotContain("data-arg=\"magic\"");
	}

	[Test]
	public async Task Guide_RawHtmlWarning_IsEscapedNotLive()
	{
		var html = new WikiMarkdigPipeline().RenderToHtml(StartupHandler.MarkdownGuideContent);

		await Assert.That(html).DoesNotContain("<script>");
	}
}
