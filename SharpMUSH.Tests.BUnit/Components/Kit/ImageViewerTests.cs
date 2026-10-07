using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// D1 §4.12 portrait viewer: a modal over the page showing one image at a time with its caption,
/// previous/next when there is more than one, and a close button that takes focus. Escape closes;
/// the arrow keys page. Only URLs the image policy accepts are shown.
/// </summary>
public class ImageViewerTests : BunitContext
{
	private static readonly IReadOnlyList<ImageViewer.Item> Three =
	[
		new("/a.jpg", "Tomas at dusk", "The quay"),
		new("/b.jpg", null, null),
		new("/c.jpg", "Lamps", "Lower Docks"),
	];

	public ImageViewerTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task Closed_RendersNothing()
	{
		var cut = Render<ImageViewer>(p => p.Add(x => x.Items, Three).Add(x => x.Open, false));
		await Assert.That(cut.Markup.Trim()).IsEmpty();
	}

	[Test]
	public async Task Open_IsAModalDialog_WithTheImageAndCaption()
	{
		var cut = Render<ImageViewer>(p => p.Add(x => x.Items, Three).Add(x => x.Open, true).Add(x => x.Index, 0));
		var dialog = cut.Find("[role='dialog']");
		await Assert.That(dialog.GetAttribute("aria-modal")).IsEqualTo("true");
		await Assert.That(cut.Find("img.kit-viewer-img").GetAttribute("src")).IsEqualTo("/a.jpg");
		await Assert.That(cut.Find("img.kit-viewer-img").GetAttribute("alt")).IsEqualTo("Tomas at dusk");
		await Assert.That(cut.Find(".kit-viewer-caption").TextContent).Contains("The quay");
		await Assert.That(cut.Find(".kit-viewer-count").TextContent).Contains("1").And.Contains("3");
	}

	[Test]
	public async Task NextAndPrevious_Wrap_AndArrowKeysPage()
	{
		var index = 0;
		var cut = Render<ImageViewer>(p => p.Add(x => x.Items, Three).Add(x => x.Open, true).Add(x => x.Index, 0)
			.Add(x => x.IndexChanged, i => index = i));
		await cut.Find("button.kit-viewer-prev").ClickAsync();
		await Assert.That(index).IsEqualTo(2);
		await Assert.That(cut.Find("img.kit-viewer-img").GetAttribute("src")).IsEqualTo("/c.jpg");
		await cut.Find("[role='dialog']").KeyDownAsync("ArrowRight");
		await Assert.That(cut.Find("img.kit-viewer-img").GetAttribute("src")).IsEqualTo("/a.jpg");
	}

	[Test]
	public async Task AnArrowKey_NeverPreventsTheNextKeysDefault()
	{
		// preventDefault is decided when the dialog renders, so a flag set by one key would swallow the
		// next Tab (focus stuck) or Enter (a capsule's click lost).
		var cut = Render<ImageViewer>(p => p.Add(x => x.Items, Three).Add(x => x.Open, true));
		await cut.Find("[role='dialog']").KeyDownAsync("ArrowRight");
		await Assert.That(cut.Find("[role='dialog']").OuterHtml.ToLowerInvariant()).DoesNotContain("preventdefault");
	}

	[Test]
	public async Task Open_TrapsFocus_AndCloseHandsItBack()
	{
		var cut = Render<ImageViewer>(p => p.Add(x => x.Items, Three).Add(x => x.Open, true));
		await Assert.That(cut.FindComponents<MudBlazor.MudFocusTrap>().Count).IsEqualTo(1);
		JSInterop.VerifyInvoke("sharpmushLayout.rememberFocus");

		await cut.Find("button.kit-viewer-close").ClickAsync();
		JSInterop.VerifyInvoke("sharpmushLayout.restoreFocus");
	}

	[Test]
	public async Task EscapeAndClose_Close()
	{
		var open = true;
		var cut = Render<ImageViewer>(p => p.Add(x => x.Items, Three).Add(x => x.Open, true).Add(x => x.OpenChanged, o => open = o));
		await cut.Find("[role='dialog']").KeyDownAsync("Escape");
		await Assert.That(open).IsFalse();

		open = true;
		await cut.Find("button.kit-viewer-close").ClickAsync();
		await Assert.That(open).IsFalse();
	}

	[Test]
	public async Task OneImage_HasNoPaging_AndARefusedUrlShowsNothing()
	{
		var one = Render<ImageViewer>(p => p.Add(x => x.Items, [new ImageViewer.Item("/a.jpg", null, null)]).Add(x => x.Open, true));
		await Assert.That(one.FindAll("button.kit-viewer-next").Count).IsEqualTo(0);

		var refused = Render<ImageViewer>(p => p.Add(x => x.Items, [new ImageViewer.Item("javascript:alert(1)", null, null)]).Add(x => x.Open, true));
		await Assert.That(refused.FindAll("img").Count).IsEqualTo(0);
	}
}
