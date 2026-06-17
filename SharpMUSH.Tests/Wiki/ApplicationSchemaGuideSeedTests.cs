using SharpMUSH.Library.Services;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Renders the seeded Help:Application Schema Guide through the real wiki pipeline to
/// guarantee its visual aids actually render: the Mermaid architecture diagram becomes a
/// <c>.mermaid</c> container for the client renderer, the SVG form mock becomes an image,
/// and the two audience sections get auto-identifier anchors so the intro jump-links resolve.
/// </summary>
public class ApplicationSchemaGuideSeedTests
{
	private static string Render() =>
		new WikiMarkdigPipeline().RenderToHtml(StartupHandler.ApplicationSchemaGuideContent);

	[Test]
	public async Task Guide_RendersWithoutError_AndContainsBothAudienceSections()
	{
		var html = Render();

		await Assert.That(html).Contains("For administrators");
		await Assert.That(html).Contains("For softcode authors");
		await Assert.That(html).Contains("<table>"); // the field-type / registry tables
	}

	[Test]
	public async Task Guide_ArchitectureDiagram_BecomesMermaidContainer()
	{
		var html = Render();

		// The ```mermaid fence must become a .mermaid block (rendered to SVG client-side),
		// not a literal code block.
		await Assert.That(html).Contains("class=\"mermaid\"");
		await Assert.That(html).Contains("flowchart LR");
	}

	[Test]
	public async Task Guide_FormMock_RendersAsSizedImage()
	{
		var html = Render();

		await Assert.That(html).Contains("src=\"/assets/docs/chargen-form-mock.svg\"");
		await Assert.That(html).Contains("class=\"wiki-img\"");
		await Assert.That(html).Contains("width=\"440\"");
	}

	[Test]
	public async Task Guide_AudienceSections_HaveAnchorIdsForJumpLinks()
	{
		var html = Render();

		// Markdig auto-identifiers give the ## headings ids that match the intro
		// [For administrators](#for-administrators) / [...](#for-softcode-authors) links.
		await Assert.That(html).Contains("id=\"for-administrators\"");
		await Assert.That(html).Contains("id=\"for-softcode-authors\"");
		await Assert.That(html).Contains("href=\"#for-administrators\"");
		await Assert.That(html).Contains("href=\"#for-softcode-authors\"");
	}

	[Test]
	public async Task Guide_WorkedExampleSoftcode_StaysLiteralCode()
	{
		var html = Render();

		// The chargen softcode lives in code fences — it must render as text, never execute
		// as a wiki directive or live HTML.
		await Assert.That(html).Contains("CHARGEN");
		await Assert.That(html).DoesNotContain("<script>");
	}
}
