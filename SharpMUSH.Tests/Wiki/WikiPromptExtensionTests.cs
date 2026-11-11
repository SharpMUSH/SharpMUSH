using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// A <c>**Fill in:**</c> block quote is a note to the page's editors: the wiki renders the page without it, and
/// without a heading it left empty.
/// </summary>
public class WikiPromptExtensionTests
{
	private static readonly WikiMarkdigPipeline Pipeline = new();

	[Test]
	public async Task Prompt_IsNotRendered_AnOrdinaryQuoteIs()
	{
		var html = Pipeline.RenderToHtml("""
			Intro.

			> **Fill in:** Something for staff.

			> A quotation stays.
			""");

		await Assert.That(html).DoesNotContain("Something for staff");
		await Assert.That(html).Contains("A quotation stays.");
		await Assert.That(html).Contains("Intro.");
	}

	[Test]
	public async Task PromptInsideAListItem_IsNotRendered()
	{
		var html = Pipeline.RenderToHtml("""
			1. Connect.
			   > **Fill in:** The address.
			2. Say hello.
			""");

		await Assert.That(html).DoesNotContain("The address");
		await Assert.That(html).Contains("Connect.");
		await Assert.That(html).Contains("Say hello.");
	}

	[Test]
	public async Task OnlyHeadingsEmptiedByAPrompt_Go()
	{
		var html = Pipeline.RenderToHtml("""
			# Page

			## Emptied
			> **Fill in:** Staff only.

			## Answered
			Text.

			## Left empty on purpose

			## Last
			> **Fill in:** One.

			Kept.
			""");

		await Assert.That(html).DoesNotContain(">Emptied</h2>");
		await Assert.That(html).Contains(">Answered</h2>");
		await Assert.That(html).Contains(">Left empty on purpose</h2>");
		await Assert.That(html).Contains(">Last</h2>");
		await Assert.That(html).Contains("Kept.");
	}

	[Test]
	public async Task Bold_FillIn_InsideAParagraph_IsText()
	{
		var html = Pipeline.RenderToHtml("""
			> Please **Fill in:** the form.
			""");

		await Assert.That(html).Contains("the form.");
		await Assert.That(WikiPromptExtension.Count("> Please **Fill in:** the form.")).IsEqualTo(0);
	}

	[Test]
	public async Task Count_CountsPromptsInTheSource()
	{
		await Assert.That(WikiPromptExtension.Count("> **Fill in:** a\n\n- x\n  > **Fill in:** b\n")).IsEqualTo(2);
		await Assert.That(WikiPromptExtension.Count(null)).IsEqualTo(0);
		await Assert.That(WikiPromptExtension.Count("No prompt.")).IsEqualTo(0);
	}

	[Test]
	public async Task PlainText_LeavesThePromptOut()
	{
		var text = Pipeline.ExtractPlainText("Shown.\n\n> **Fill in:** Hidden.");

		await Assert.That(text).IsEqualTo("Shown.");
	}
}
