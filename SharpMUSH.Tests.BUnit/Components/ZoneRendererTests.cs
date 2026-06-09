using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Layout;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// BUnit component tests for <see cref="ZoneRenderer"/>.
/// </summary>
public abstract class ZoneRendererTestBase : BunitContext
{
	protected ZoneRendererTestBase()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	protected static LayoutConfiguration EmptyLayout() =>
		new(
			Zones: new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.TopBar] = [],
				[WidgetZone.LeftSidebar] = [],
				[WidgetZone.RightSidebar] = [],
				[WidgetZone.MainContent] = [],
				[WidgetZone.Footer] = []
			},
			Settings: new LayoutSettings(false, false));

	protected static IWidgetRegistry EmptyRegistry()
	{
		var reg = Substitute.For<IWidgetRegistry>();
		reg.GetWidget(Arg.Any<string>()).Returns((IPortalWidget?)null);
		return reg;
	}
}

public class ZoneRendererEmptyTests : ZoneRendererTestBase
{
	[TUnit.Core.Test]
	public async Task ZoneRenderer_EmptyZone_RendersNoContent()
	{
		Services.AddSingleton(EmptyRegistry());
		var layout = EmptyLayout();

		var cut = Render<ZoneRenderer>(p => p
			.Add(c => c.Zone, WidgetZone.TopBar)
			.Add(c => c.Layout, layout));

		// Nothing rendered — no DynamicComponent or MudAlert
		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}
}

public class ZoneRendererNullLayoutTests : ZoneRendererTestBase
{
	[TUnit.Core.Test]
	public async Task ZoneRenderer_NullLayout_RendersNothing()
	{
		Services.AddSingleton(EmptyRegistry());

		var cut = Render<ZoneRenderer>(p => p
			.Add(c => c.Zone, WidgetZone.TopBar)
			.Add(c => c.Layout, (LayoutConfiguration?)null));

		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}
}

public class ZoneRendererUnknownWidgetTests : ZoneRendererTestBase
{
	[TUnit.Core.Test]
	public async Task ZoneRenderer_UnknownWidgetName_RendersNothing()
	{
		// Registry returns null for any lookup → descriptor not found → DynamicComponent not emitted
		Services.AddSingleton(EmptyRegistry());

		var layout = new LayoutConfiguration(
			Zones: new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.TopBar] = [new WidgetPlacement("DoesNotExist", 0, null)],
				[WidgetZone.LeftSidebar] = [],
				[WidgetZone.RightSidebar] = [],
				[WidgetZone.MainContent] = [],
				[WidgetZone.Footer] = []
			},
			Settings: new LayoutSettings(false, false));

		var cut = Render<ZoneRenderer>(p => p
			.Add(c => c.Zone, WidgetZone.TopBar)
			.Add(c => c.Layout, layout));

		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}
}
