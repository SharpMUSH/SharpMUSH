using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// The first image of a wiki page is its banner (D1 README §6.2): the server reports it on the
/// DTO, and the client removes that one copy from the rendered body so it does not show twice.
/// Both read the rendered HTML, which is what the store keeps and what the body shows, so the
/// image found and the image removed are the same element however its URL was escaped.
/// </summary>
public class WikiImagesTests
{
	[Test]
	public async Task FirstImageUrl_FindsThePlainImage()
		=> await Assert.That(WikiImages.FirstImageUrl("<p>Intro</p><p><img class=\"wiki-img\" src=\"/api/wiki-assets/abc/quay.jpg\" alt=\"The quay\" loading=\"lazy\" /></p><p>More</p>"))
			.IsEqualTo("/api/wiki-assets/abc/quay.jpg");

	[Test]
	public async Task FirstImageUrl_DecodesEntities_AndKeepsPercentEncoding()
	{
		// Markdig writes src through WriteEscapeUrl: & becomes &amp;, non-ASCII and spaces are percent-encoded.
		await Assert.That(WikiImages.FirstImageUrl("<p><img src=\"/img/x.jpg?w=800&amp;h=400\" alt=\"\"></p>"))
			.IsEqualTo("/img/x.jpg?w=800&h=400");
		await Assert.That(WikiImages.FirstImageUrl("<p><img src=\"/api/wiki-assets/1/caf%C3%A9%20quay.png\" alt=\"\"></p>"))
			.IsEqualTo("/api/wiki-assets/1/caf%C3%A9%20quay.png");
	}

	[Test]
	public async Task FirstImageUrl_TakesOnlyTheFirst()
		=> await Assert.That(WikiImages.FirstImageUrl("<p><img src=\"/one.jpg\" alt=\"a\" title=\"Title\"> then <img src=\"/two.jpg\" alt=\"b\"></p>"))
			.IsEqualTo("/one.jpg");

	[Test]
	public async Task FirstImageUrl_IgnoresAnImageShownAsCode()
		=> await Assert.That(WikiImages.FirstImageUrl("<pre><code>&lt;img src=\"/in-code.jpg\"&gt;</code></pre><p>Text <code>&lt;img src=\"/inline.jpg\"&gt;</code> end</p>"))
			.IsNull();

	[Test]
	public async Task FirstImageUrl_NoImage_IsNull()
		=> await Assert.That(WikiImages.FirstImageUrl("<h1>Title</h1><p>Just <a href=\"/wiki/main/x\">a link</a>.</p>")).IsNull();

	[Test]
	public async Task FirstImageUrl_EmptyOrNull_IsNull()
	{
		await Assert.That(WikiImages.FirstImageUrl("")).IsNull();
		await Assert.That(WikiImages.FirstImageUrl(null)).IsNull();
	}

	[Test]
	public async Task StripFirstImage_RemovesTheImgAndItsEmptyParagraph_Once()
	{
		const string html = "<p><img class=\"wiki-img\" src=\"/one.jpg\" alt=\"a\" loading=\"lazy\" /></p><p>text</p><p><img src=\"/one.jpg\" alt=\"again\"></p>";
		var stripped = WikiImages.StripFirstImage(html, "/one.jpg");
		await Assert.That(stripped).IsEqualTo("<p>text</p><p><img src=\"/one.jpg\" alt=\"again\"></p>")
			.Because("the paragraph the image sat in alone would otherwise open the body with a blank line");
	}

	[Test]
	public async Task StripFirstImage_MatchesTheEscapedSrc()
	{
		await Assert.That(WikiImages.StripFirstImage("<p><img src=\"/img/x.jpg?w=800&amp;h=400\" alt=\"\"></p><p>t</p>", "/img/x.jpg?w=800&h=400"))
			.IsEqualTo("<p>t</p>");
		await Assert.That(WikiImages.StripFirstImage("<p><img src=\"/a/caf%C3%A9%20quay.png\" alt=\"\"></p><p>t</p>", "/a/caf%C3%A9%20quay.png"))
			.IsEqualTo("<p>t</p>");
	}

	[Test]
	public async Task StripFirstImage_KeepsAParagraphThatHasOtherContent()
		=> await Assert.That(WikiImages.StripFirstImage("<p>Look: <img src=\"/one.jpg\" alt=\"\"> here</p>", "/one.jpg"))
			.IsEqualTo("<p>Look:  here</p>");

	[Test]
	public async Task StripFirstImage_LeavesOtherImagesAlone()
	{
		const string html = "<p><img src=\"/two.jpg\"></p>";
		await Assert.That(WikiImages.StripFirstImage(html, "/one.jpg")).IsEqualTo(html);
	}

	// --- LeadImageUrl: only an image that opens the page becomes its banner -----------------

	[Test]
	[Arguments("<p><img src=\"/one.jpg\" alt=\"\"></p><p>text</p>")]
	[Arguments("<img src=\"/one.jpg\"><p>text</p>")]
	[Arguments("\n<p>\n<img src=\"/one.jpg\"></p>")]
	[Arguments("<div class=\"center\">\n<p><img src=\"/one.jpg\"></p>\n<h1>Welcome</h1>\n</div>")]
	public async Task LeadImageUrl_ImageOpeningThePage_IsTheLead(string html)
		=> await Assert.That(WikiImages.LeadImageUrl(html)).IsEqualTo("/one.jpg");

	/// <summary>The Application Schema Guide's mock-up sits halfway down; it illustrates the text it follows.</summary>
	[Test]
	[Arguments("<p>Intro.</p><p><img src=\"/one.jpg\"></p>")]
	[Arguments("<h2>Heading</h2><p><img src=\"/one.jpg\"></p>")]
	[Arguments("<p>Look: <img src=\"/one.jpg\"></p>")]
	[Arguments("<div class=\"center\">\n<p>Intro.</p>\n<p><img src=\"/one.jpg\"></p>\n</div>")]
	[Arguments("<div class=\"md-flex\">\n<p><img src=\"/one.jpg\"></p>\n</div>")]
	[Arguments("<p>no images</p>")]
	[Arguments("")]
	[Arguments(null)]
	public async Task LeadImageUrl_ImageAfterText_IsNotTheLead(string? html)
		=> await Assert.That(WikiImages.LeadImageUrl(html)).IsNull();
}
