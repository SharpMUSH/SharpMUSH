using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Play;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.3 scene card header (boards 01, 04, 05): the title, the sub-line the page passes, the
/// Story | Terminal radiogroup (only in a scene, each view's description its tooltip) and the focus button.
/// </summary>
public class PlaySceneCardTests : BunitContext
{
	public PlaySceneCardTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<PlaySceneCard> RenderCard(bool inScene = true, PlayView view = PlayView.Story, bool focus = false,
		Action<PlayView>? onView = null, Action<bool>? onFocus = null, string? subtitle = null) =>
		Render<PlaySceneCard>(p => p
			.Add(x => x.Title, "Salt Market at Dusk")
			.Add(x => x.Subtitle, subtitle)
			.Add(x => x.InScene, inScene)
			.Add(x => x.View, view)
			.Add(x => x.ViewChanged, v => onView?.Invoke(v))
			.Add(x => x.Focus, focus)
			.Add(x => x.FocusChanged, f => onFocus?.Invoke(f))
			.Add(x => x.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p id=\"body\">body</p>"))));

	[Test]
	public async Task TheSubLine_IsWhatThePagePasses_AndThereIsNoneWithout()
	{
		var cut = RenderCard(subtitle: "Lower Docks");
		await Assert.That(cut.Find(".scene-card-title").TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(cut.Find(".scene-card-sub").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find("#body")).IsNotNull();

		var bare = RenderCard();
		await Assert.That(bare.FindAll(".scene-card-sub").Count).IsEqualTo(0).Because("no empty line takes the header's height");
	}

	[Test]
	public async Task AHeaderImage_SitsBehindTheHeader_UnderAScrim()
	{
		var cut = Render<PlaySceneCard>(p => p
			.Add(x => x.Title, "Ilsa Varn")
			.Add(x => x.HeaderImage, "https://localhost:8081/r/docks.jpg"));
		await Assert.That(cut.Find(".scene-card-head").ClassList).Contains("scene-card-head--image");
		await Assert.That(cut.Find(".scene-card-head-img").GetAttribute("alt")).IsEqualTo("").Because("decorative: the sub-line names the room");
		await Assert.That(cut.FindAll(".scene-card-head-scrim").Count).IsEqualTo(1);

		var bare = RenderCard();
		await Assert.That(bare.FindAll(".scene-card-head-img").Count).IsEqualTo(0);
		await Assert.That(bare.Find(".scene-card-head").ClassList).DoesNotContain("scene-card-head--image");
	}

	[Test]
	public async Task EachViewsDescription_IsItsRadiosTooltip()
	{
		var radios = RenderCard().FindAll("[role='radio']");
		await Assert.That(radios[0].GetAttribute("title")).IsEqualTo("Scene · logged to the scene archive");
		await Assert.That(radios[1].GetAttribute("title")).IsEqualTo("Full output · channels and pages included");
	}

	[Test]
	public async Task TheSwitch_IsARadiogroup_AndChoosing()
	{
		PlayView? chosen = null;
		var cut = RenderCard(onView: v => chosen = v);
		var group = cut.Find("[role='radiogroup']");
		await Assert.That(group.GetAttribute("aria-label")).IsEqualTo("View");
		var radios = cut.FindAll("[role='radio']");
		await Assert.That(radios[0].TextContent).IsEqualTo("Story");
		await Assert.That(radios[0].GetAttribute("aria-checked")).IsEqualTo("true");
		await Assert.That(radios[0].GetAttribute("tabindex")).IsEqualTo("0");
		await Assert.That(radios[1].GetAttribute("aria-checked")).IsEqualTo("false");
		await Assert.That(radios[1].GetAttribute("tabindex")).IsEqualTo("-1");
		radios[1].Click();
		await Assert.That(chosen).IsEqualTo(PlayView.Terminal);
	}

	[Test]
	public async Task ArrowKeys_MoveTheChoice()
	{
		PlayView? chosen = null;
		var cut = RenderCard(onView: v => chosen = v);
		cut.FindAll("[role='radio']")[0].KeyDown("ArrowRight");
		await Assert.That(chosen).IsEqualTo(PlayView.Terminal);
	}

	[Test]
	public async Task OutsideAScene_ThereIsNoSwitch()
	{
		var cut = RenderCard(inScene: false, view: PlayView.Terminal);
		await Assert.That(cut.FindAll("[role='radiogroup']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Focus_IsAToggleButtonInTheHeader()
	{
		bool? focus = null;
		var cut = RenderCard(onFocus: f => focus = f);
		var button = cut.Find("button.scene-card-focus");
		await Assert.That(button.GetAttribute("aria-pressed")).IsEqualTo("false");
		await Assert.That(button.GetAttribute("aria-label")).IsEqualTo("Focus mode");
		button.Click();
		await Assert.That(focus).IsTrue();
		var on = RenderCard(focus: true);
		await Assert.That(on.Find("button.scene-card-focus").GetAttribute("aria-pressed")).IsEqualTo("true");
		await Assert.That(on.Find("button.scene-card-focus").GetAttribute("aria-label")).IsEqualTo("Exit focus mode");
	}
}
