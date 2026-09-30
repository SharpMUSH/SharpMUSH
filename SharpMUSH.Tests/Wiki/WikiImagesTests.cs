using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// The first image of a wiki page is its banner (D1 README §6.2): the server reports it on the
/// DTO, and the client removes that one copy from the rendered body so it does not show twice.
/// </summary>
public class WikiImagesTests
{
	[Test]
	public async Task FirstImageUrl_FindsAPlainImage()
		=> await Assert.That(WikiImages.FirstImageUrl("Intro\n\n![The quay](/api/wiki-assets/abc/quay.jpg)\n\nMore"))
			.IsEqualTo("/api/wiki-assets/abc/quay.jpg");

	[Test]
	public async Task FirstImageUrl_HandlesATitle_AndTakesOnlyTheFirst()
		=> await Assert.That(WikiImages.FirstImageUrl("![a](/one.jpg \"Title\") then ![b](/two.jpg)"))
			.IsEqualTo("/one.jpg");

	[Test]
	public async Task FirstImageUrl_IgnoresImagesInsideCode()
		=> await Assert.That(WikiImages.FirstImageUrl("```\n![x](/in-code.jpg)\n```\n\nText `![y](/inline.jpg)` end"))
			.IsNull();

	[Test]
	public async Task FirstImageUrl_NoImage_IsNull()
		=> await Assert.That(WikiImages.FirstImageUrl("# Title\n\nJust [a link](/wiki/main/general/x).")).IsNull();

	[Test]
	public async Task FirstImageUrl_EmptyOrNull_IsNull()
	{
		await Assert.That(WikiImages.FirstImageUrl("")).IsNull();
		await Assert.That(WikiImages.FirstImageUrl(null)).IsNull();
	}

	[Test]
	public async Task StripFirstImage_RemovesOnlyTheImgWithThatSrc_Once()
	{
		const string html = "<p><img class=\"wiki-img\" src=\"/one.jpg\" alt=\"a\" loading=\"lazy\" /></p><p>text</p><p><img src=\"/one.jpg\" alt=\"again\"></p>";
		var stripped = WikiImages.StripFirstImage(html, "/one.jpg");
		await Assert.That(stripped).IsEqualTo("<p></p><p>text</p><p><img src=\"/one.jpg\" alt=\"again\"></p>");
	}

	[Test]
	public async Task StripFirstImage_LeavesOtherImagesAlone()
	{
		const string html = "<p><img src=\"/two.jpg\"></p>";
		await Assert.That(WikiImages.StripFirstImage(html, "/one.jpg")).IsEqualTo(html);
	}
}
