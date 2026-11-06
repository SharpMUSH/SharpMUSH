using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// The one empty state every empty list in the portal uses: an icon, one sentence, and at most one action.
/// </summary>
public class EmptyStateTests : BunitContext
{
	[Test]
	public async Task RendersTheIconTheSentenceAndNoActionRow()
	{
		var cut = Render<EmptyState>(p => p
			.Add(x => x.Icon, Icons.Material.Outlined.TheaterComedy)
			.AddChildContent("No scenes yet."));

		var root = cut.Find(".kit-empty");
		await Assert.That(root.GetAttribute("data-empty")).IsEqualTo("empty");
		await Assert.That(cut.Find(".kit-empty-text").TextContent).IsEqualTo("No scenes yet.");
		await Assert.That(cut.Find(".kit-empty-icon").GetAttribute("aria-hidden")).IsEqualTo("true");
		await Assert.That(cut.Find(".kit-empty-icon svg").InnerHtml).IsEqualTo(Svg(Icons.Material.Outlined.TheaterComedy));
		await Assert.That(cut.FindAll(".kit-empty-action").Count).IsEqualTo(0);
	}

	[Test]
	public async Task WithoutAnIcon_ShowsAnEmptyTray()
	{
		var cut = Render<EmptyState>(p => p.AddChildContent("Nothing here."));
		await Assert.That(cut.Find(".kit-empty-icon svg").InnerHtml).IsEqualTo(Svg(Icons.Material.Outlined.Inbox));
	}

	[Test]
	public async Task Failed_SaysTheListCouldNotBeRead()
	{
		var cut = Render<EmptyState>(p => p.Add(x => x.Failed, true).AddChildContent("Unavailable."));

		var root = cut.Find(".kit-empty");
		await Assert.That(root.ClassList).Contains("kit-empty--failed");
		await Assert.That(root.GetAttribute("data-empty")).IsEqualTo("failed");
		await Assert.That(cut.Find(".kit-empty-icon svg").InnerHtml).IsEqualTo(Svg(Icons.Material.Outlined.CloudOff));
	}

	[Test]
	public async Task TheActionRendersUnderTheSentence_AndRuns()
	{
		var clicks = 0;
		RenderFragment action = b =>
		{
			b.OpenComponent<CapsuleButton>(0);
			b.AddAttribute(1, nameof(CapsuleButton.OnClick), EventCallback.Factory.Create(this, () => clicks++));
			b.AddAttribute(2, nameof(CapsuleButton.ChildContent), (RenderFragment)(c => c.AddContent(0, "Start scene")));
			b.CloseComponent();
		};
		var cut = Render<EmptyState>(p => p.Add(x => x.Action, action).AddChildContent("No scenes yet."));

		var button = cut.Find(".kit-empty-action button");
		await Assert.That(button.TextContent).Contains("Start scene");
		await button.ClickAsync();
		await Assert.That(clicks).IsEqualTo(1);
	}

	[Test]
	public async Task Compact_IsTheInlineForm_AndKeepsTheCallersClass()
	{
		var cut = Render<EmptyState>(p => p
			.Add(x => x.Compact, true)
			.Add(x => x.Class, "here-empty")
			.AddChildContent("Nobody else is here."));

		var root = cut.Find(".kit-empty");
		await Assert.That(root.ClassList).Contains("kit-empty--compact");
		await Assert.That(root.ClassList).Contains("here-empty");
		await Assert.That(root.TextContent).IsEqualTo("Nobody else is here.");
	}

	[Test]
	public async Task OtherAttributesReachTheRoot()
	{
		var cut = Render<EmptyState>(p => p
			.AddUnmatched("aria-hidden", "true")
			.AddUnmatched("id", "library-empty")
			.AddChildContent("No pictures."));

		var root = cut.Find(".kit-empty");
		await Assert.That(root.GetAttribute("aria-hidden")).IsEqualTo("true");
		await Assert.That(root.Id).IsEqualTo("library-empty");
	}

	/// <summary>The markup MudIcon draws for an icon, to compare against.</summary>
	private string Svg(string icon) => Render<MudIcon>(p => p.Add(x => x.Icon, icon)).Find("svg").InnerHtml;
}
