using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// A picture whose size the game knows is saved with its shape, so the page holds the picture's space while it
/// loads instead of pushing everything below it down when it arrives.
/// </summary>
public class WikiImageSizeTests
{
	private sealed class KnownSizes(Dictionary<string, ImageSize> sizes) : IWikiImageSizes
	{
		public Task<Found<ImageSize>> FindAsync(string url, CancellationToken ct = default)
			=> Task.FromResult<Found<ImageSize>>(sizes.TryGetValue(url, out var size) ? size : new NotFound());
	}

	private static readonly KnownSizes Sizes = new(new()
	{
		["/api/wiki-assets/abc/quay.png"] = new ImageSize(1200, 600),
		["/assets/Logo.svg"] = new ImageSize(512, 512),
	});

	[Test]
	public async Task AnUnsizedPicture_TakesItsOwnWidthHeightAndShape()
	{
		var html = await new WikiMarkdigPipeline().RenderToHtmlAsync("![The quay](/api/wiki-assets/abc/quay.png)", Sizes);

		await Assert.That(html).Contains("class=\"wiki-img wiki-img-sized\"");
		await Assert.That(html).Contains("width=\"1200\" height=\"600\"");
		await Assert.That(html).Contains("style=\"aspect-ratio: 1200 / 600\"");
	}

	[Test]
	public async Task APictureTheMarkdownSizes_KeepsTheAuthorsWidth_AndTakesOnlyTheShape()
	{
		var html = await new WikiMarkdigPipeline().RenderToHtmlAsync("![Logo](/assets/Logo.svg){width=20%}", Sizes);

		await Assert.That(html).Contains("width=\"20%\"");
		await Assert.That(html).DoesNotContain("height=");
		await Assert.That(html).DoesNotContain("wiki-img-sized");
		await Assert.That(html).Contains("style=\"aspect-ratio: 512 / 512\"");
	}

	[Test]
	[Arguments("![Elsewhere](https://pictures.example/cat.png)")]
	[Arguments("![Not stored](/api/wiki-assets/zzz/gone.png)")]
	[Arguments("Text with no picture and a [link](/wiki/main/home).")]
	public async Task WhatTheGameDoesNotMeasure_RendersAsBefore(string markdown)
	{
		var pipeline = new WikiMarkdigPipeline();
		var rendered = await pipeline.RenderToHtmlAsync(markdown, Sizes);

		await Assert.That(rendered).IsEqualTo(pipeline.RenderToHtml(markdown));
	}

	[Test]
	public async Task ASavedPage_KeepsThePicturesShape()
	{
		var wiki = new WikiStoreService(new InMemoryWikiStore(), new WikiMarkdigPipeline(), Sizes);

		var page = (await wiki.CreateAsync("Quay", "Before\n\n![The quay](/api/wiki-assets/abc/quay.png)\n\nAfter", "#1"))
			.Expect<WikiPage>();
		await Assert.That(page.RenderedHtml).Contains("width=\"1200\" height=\"600\"");

		var edited = (await wiki.UpdateAsync(page.Id, "![Logo](/assets/Logo.svg){width=20%}", "#1")).Expect<WikiPage>();
		await Assert.That(edited.RenderedHtml).Contains("aspect-ratio: 512 / 512");
	}
}
