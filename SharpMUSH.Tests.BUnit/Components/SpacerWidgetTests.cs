using System.Text.Json;
using Bunit;
using SharpMUSH.Client.Components.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Confirms the Spacer widget renders an empty block at the default height, honors a configured
/// height, and falls back to the default for missing/invalid config.
/// </summary>
public class SpacerWidgetTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task NoConfig_RendersDefaultHeight()
	{
		var cut = Render<SpacerWidget>();
		await Assert.That(cut.Markup).Contains("height:24px");
	}

	[TUnit.Core.Test]
	public async Task Config_AppliesHeight()
	{
		var cfg = JsonSerializer.SerializeToElement(new { height = 80 });
		var cut = Render<SpacerWidget>(p => p.Add(x => x.Config, cfg));
		await Assert.That(cut.Markup).Contains("height:80px");
	}

	[TUnit.Core.Test]
	public async Task NonPositiveHeight_FallsBackToDefault()
	{
		var cfg = JsonSerializer.SerializeToElement(new { height = 0 });
		var cut = Render<SpacerWidget>(p => p.Add(x => x.Config, cfg));
		await Assert.That(cut.Markup).Contains("height:24px");
	}
}
