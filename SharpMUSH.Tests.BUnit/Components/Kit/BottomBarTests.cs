using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class BottomBarTests : BunitContext
{
	[Test]
	public async Task BottomBar_WrapsContent()
	{
		var cut = Render<BottomBar>(p => p.AddChildContent("<input />"));
		await Assert.That(cut.Find(".kit-bottom-bar input")).IsNotNull();
	}

	[Test]
	public async Task BottomBar_PassesClassThrough()
	{
		var cut = Render<BottomBar>(p => p.Add(x => x.Class, "composer").AddChildContent("x"));
		await Assert.That(cut.Find(".kit-bottom-bar").ClassList).Contains("composer");
	}

	[Test]
	public async Task TypeChips_IsARadioGroup_AndSelectionChanges()
	{
		string? picked = null;
		var items = new List<(string, string)> { ("pose", "Pose"), ("say", "Say"), ("ooc", "OOC"), ("cmd", "Command") };
		var cut = Render<TypeChips>(p => p.Add(x => x.Items, items).Add(x => x.Selected, "pose").Add(x => x.AriaLabel, "Message type").Add(x => x.SelectedChanged, v => picked = v));
		await Assert.That(cut.Find("[role=radiogroup]").GetAttribute("aria-label")).IsEqualTo("Message type");
		var radios = cut.FindAll("[role=radio]");
		await Assert.That(radios.Count).IsEqualTo(4);
		await Assert.That(radios[0].GetAttribute("aria-checked")).IsEqualTo("true");
		await Assert.That(radios[0].ClassList).Contains("kit-chip--on");
		await Assert.That(radios[1].GetAttribute("aria-checked")).IsEqualTo("false");
		radios[2].Click();
		await Assert.That(picked).IsEqualTo("ooc");
	}

	[Test]
	public async Task TypeChips_WithoutAriaLabel_OmitsTheAttribute()
	{
		var items = new List<(string, string)> { ("pose", "Pose") };
		var cut = Render<TypeChips>(p => p.Add(x => x.Items, items).Add(x => x.Selected, "pose"));
		await Assert.That(cut.Find("[role=radiogroup]").HasAttribute("aria-label")).IsFalse();
	}

	[Test]
	public async Task TypeChips_AreRealButtons()
	{
		var items = new List<(string, string)> { ("pose", "Pose") };
		var cut = Render<TypeChips>(p => p.Add(x => x.Items, items).Add(x => x.Selected, "pose"));
		await Assert.That(cut.Find("button.kit-chip").GetAttribute("type")).IsEqualTo("button");
	}
}
