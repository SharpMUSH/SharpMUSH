using SharpMUSH.Server.Resources;
using SharpMUSH.Library.Services;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Renders the seeded Help:Application Schema Guide through the real wiki pipeline to
/// guarantee its visual aids actually render: the Mermaid diagrams become <c>.mermaid</c>
/// containers for the client renderer, the SVG form mock becomes an image, and every in-page
/// link lands on a heading's auto-identifier anchor.
/// </summary>
public class ApplicationSchemaGuideSeedTests
{
	private static string Render() =>
		new WikiMarkdigPipeline().RenderToHtml(SeededWikiPages.ApplicationSchemaGuide);

	[Test]
	public async Task Guide_OpensWithCrystalSchemaArtwork()
	{
		var html = Render();

		await Assert.That(WikiImages.LeadImageUrl(html))
			.IsEqualTo("/assets/presets/wiki/application-schema-guide.webp");
		await Assert.That(html)
			.Contains("alt=\"Colored crystal types pass through schema validation and composition into an interconnected lattice\"");
	}

	[Test]
	public async Task Guide_RendersWithoutError_AndContainsEveryAudienceSection()
	{
		var html = Render();

		await Assert.That(html).Contains("Using applications");
		await Assert.That(html).Contains("Adding an application");
		await Assert.That(html).Contains("Building your own application");
		await Assert.That(html).Contains("<table>"); // the field-type / registry tables
	}

	[Test]
	public async Task Guide_Diagrams_BecomeMermaidContainers()
	{
		var html = Render();

		// Each ```mermaid fence must become a .mermaid block (rendered to SVG client-side),
		// not a literal code block.
		await Assert.That(html.Split("class=\"mermaid\"").Length - 1).IsEqualTo(4);
		await Assert.That(html).Contains("sequenceDiagram");
		await Assert.That(html).Contains("flowchart LR");
		await Assert.That(html).Contains("flowchart TD");
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
	public async Task Guide_JumpLinks_AllHaveTargets()
	{
		var html = Render();

		// Markdig auto-identifiers give the headings ids; every in-page link must name one of them.
		var ids = System.Text.RegularExpressions.Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
		var targets = System.Text.RegularExpressions.Regex.Matches(html, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

		await Assert.That(targets).Contains("using-applications");
		await Assert.That(targets).Contains("adding-an-application");
		await Assert.That(targets).Contains("building-your-own-application");
		await Assert.That(targets.Where(t => !ids.Contains(t))).IsEmpty();
	}

	[Test]
	public async Task Guide_IsPlainAscii()
	{
		await Assert.That(SeededWikiPages.ApplicationSchemaGuide.All(char.IsAscii)).IsTrue();
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
