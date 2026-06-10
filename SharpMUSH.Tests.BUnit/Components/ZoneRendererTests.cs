using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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

/// <summary>Widget component that always throws during initialization.</summary>
file sealed class ThrowingWidget : ComponentBase
{
	[Parameter] public object? Config { get; set; }
	[Parameter] public string? Zone { get; set; }

	protected override void OnInitialized() =>
		throw new InvalidOperationException("ThrowingWidget always crashes.");
}

/// <summary>Widget component that renders a recognizable marker.</summary>
file sealed class HealthyWidget : ComponentBase
{
	[Parameter] public object? Config { get; set; }
	[Parameter] public string? Zone { get; set; }

	protected override void BuildRenderTree(RenderTreeBuilder builder) =>
		builder.AddMarkupContent(0, "<div class=\"healthy-widget\">healthy-widget-content</div>");
}

public class ZoneRendererErrorBoundaryTests : ZoneRendererTestBase
{
	private static IWidgetRegistry RegistryWith(params (string Name, Type ComponentType)[] widgets)
	{
		var reg = Substitute.For<IWidgetRegistry>();
		reg.GetWidget(Arg.Any<string>()).Returns((IPortalWidget?)null);
		foreach (var (name, componentType) in widgets)
		{
			var widget = Substitute.For<IPortalWidget>();
			widget.Name.Returns(name);
			widget.ComponentType.Returns(componentType);
			reg.GetWidget(name).Returns(widget);
		}
		return reg;
	}

	private static LayoutConfiguration LayoutWith(params string[] widgetNames) =>
		new(
			Zones: new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.TopBar] = widgetNames.Select((n, i) => new WidgetPlacement(n, i, null)).ToList(),
				[WidgetZone.LeftSidebar] = [],
				[WidgetZone.RightSidebar] = [],
				[WidgetZone.MainContent] = [],
				[WidgetZone.Footer] = []
			},
			Settings: new LayoutSettings(false, false));

	[TUnit.Core.Test]
	public async Task ZoneRenderer_ThrowingWidget_DoesNotKillHealthyWidgetInSameZone()
	{
		Services.AddSingleton(RegistryWith(
			("Thrower", typeof(ThrowingWidget)),
			("Healthy", typeof(HealthyWidget))));

		var cut = Render<ZoneRenderer>(p => p
			.Add(c => c.Zone, WidgetZone.TopBar)
			.Add(c => c.Layout, LayoutWith("Thrower", "Healthy")));

		// The healthy widget still rendered despite its sibling crashing …
		await Assert.That(cut.Markup).Contains("healthy-widget-content");
		// … and the crash surfaced as the per-widget error alert, naming the widget.
		await Assert.That(cut.Markup).Contains("failed to render");
		await Assert.That(cut.Markup).Contains("Thrower");
	}

	[TUnit.Core.Test]
	public async Task ZoneRenderer_ThrowingWidget_ShowsErrorAlertInsteadOfPropagating()
	{
		Services.AddSingleton(RegistryWith(("Thrower", typeof(ThrowingWidget))));

		// Rendering must not throw — the ErrorBoundary contains the failure.
		var cut = Render<ZoneRenderer>(p => p
			.Add(c => c.Zone, WidgetZone.TopBar)
			.Add(c => c.Layout, LayoutWith("Thrower")));

		await Assert.That(cut.Markup).Contains("failed to render");
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
