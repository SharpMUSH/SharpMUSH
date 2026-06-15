using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Layout;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Confirms <see cref="ZoneRenderer"/> grid mode lays widgets out in a 12-column grid honoring each
/// placement's <c>Span</c>, and that list mode does not.
/// </summary>
public class ZoneRendererGridTests : BunitContext
{
	public ZoneRendererGridTests()
	{
		var registry = new WidgetRegistry();
		registry.Register(new WelcomeTextWidgetDescriptor());
		Services.AddSingleton<IWidgetRegistry>(registry);
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static LayoutConfiguration TwoPlacements() => new(
		new Dictionary<WidgetZone, List<WidgetPlacement>>
		{
			[WidgetZone.MainContent] =
			[
				new WidgetPlacement("WelcomeText", 0, JsonSerializer.SerializeToElement(new { markdown = "# AAA" }), Span: 12),
				new WidgetPlacement("WelcomeText", 1, JsonSerializer.SerializeToElement(new { markdown = "# BBB" }), Span: 6)
			]
		},
		new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false));

	[TUnit.Core.Test]
	public async Task GridMode_EmitsGridContainerAndPerItemSpan()
	{
		var cut = Render<ZoneRenderer>(p => p
			.Add(x => x.Zone, WidgetZone.MainContent)
			.Add(x => x.Layout, TwoPlacements())
			.Add(x => x.Grid, true));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("zone-grid");
		await Assert.That(markup).Contains("grid-column:span 12");
		await Assert.That(markup).Contains("grid-column:span 6");
		await Assert.That(markup).Contains("AAA");
		await Assert.That(markup).Contains("BBB");
	}

	[TUnit.Core.Test]
	public async Task ListMode_DoesNotEmitGridCells()
	{
		var cut = Render<ZoneRenderer>(p => p
			.Add(x => x.Zone, WidgetZone.MainContent)
			.Add(x => x.Layout, TwoPlacements())
			.Add(x => x.Grid, false));

		await Assert.That(cut.Markup).DoesNotContain("zone-grid-cell");
		await Assert.That(cut.Markup).Contains("AAA");
	}
}
