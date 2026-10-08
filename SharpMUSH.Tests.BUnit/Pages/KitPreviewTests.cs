using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Pages.Admin;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The staff-only /admin/kit page exists so the D1 kit can be screenshotted against
/// docs/design/d1/boards/20-patterns.png. This pins that it renders one of every kit piece.
/// </summary>
public class KitPreviewTests : TrackingBunitContext
{
	public KitPreviewTests()
	{
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task RendersOneOfEveryKitPiece()
	{
		var cut = Render<KitPreview>();
		foreach (var cls in new[]
		{
			".kit-page-head", ".kit-section-label", ".kit-row", ".kit-banner", ".kit-banner-strip", ".kit-card",
			".kit-portrait", ".kit-tile", ".kit-pill", ".mention", ".kit-ooc", ".kit-bottom-bar", ".kit-chips", ".kit-capsule",
		})
		{
			await Assert.That(cut.FindAll(cls).Count).IsGreaterThan(0).Because(cls);
		}
	}

	[Test]
	public async Task UsesTheTwoLocalizedStrings()
	{
		var cut = Render<KitPreview>();
		await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Design kit");
		await Assert.That(cut.Find(".kit-page-desc").TextContent).Contains("boards");
		await Assert.That(cut.FindAll("[role=radiogroup]").Select(g => g.GetAttribute("aria-label"))).Contains("Message type");
	}
}
