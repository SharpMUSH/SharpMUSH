using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// BUnit component tests for <see cref="ZoneRenderer"/>.
/// </summary>
public abstract class ZoneRendererTestBase : TrackingBunitContext
{
	protected ZoneRendererTestBase()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
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
			widget.Feature.Returns((string?)null);
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

		await Assert.That(cut.Markup).Contains("healthy-widget-content");
		await Assert.That(cut.Markup).Contains("WidWidgetRenderFailed");
		await Assert.That(cut.Markup).Contains("Thrower");
	}

	[TUnit.Core.Test]
	public async Task ZoneRenderer_ThrowingWidget_ShowsErrorAlertInsteadOfPropagating()
	{
		Services.AddSingleton(RegistryWith(("Thrower", typeof(ThrowingWidget))));

		var cut = Render<ZoneRenderer>(p => p
			.Add(c => c.Zone, WidgetZone.TopBar)
			.Add(c => c.Layout, LayoutWith("Thrower")));

		await Assert.That(cut.Markup).Contains("WidWidgetRenderFailed");
	}
}

/// <summary>
/// A widget of an optional application is left out while the game does not have it on: without the application's
/// plugin, such as the Scene plugin, nothing serves what it shows.
/// </summary>
public class ZoneRendererFeatureTests : ZoneRendererTestBase
{
	private StubServerInfoService Register(IReadOnlyList<string> features)
	{
		var reg = Substitute.For<IWidgetRegistry>();
		var healthy = Substitute.For<IPortalWidget>();
		healthy.Name.Returns("Healthy");
		healthy.ComponentType.Returns(typeof(HealthyWidget));
		healthy.Feature.Returns("scenes");
		reg.GetWidget("Healthy").Returns(healthy);
		Services.AddSingleton(reg);
		var serverInfo = new StubServerInfoService(guestsEnabled: true, features: features);
		Services.AddSingleton<ServerInfoService>(serverInfo);
		return serverInfo;
	}

	private static LayoutConfiguration Layout() => new(
		Zones: new Dictionary<WidgetZone, List<WidgetPlacement>>
		{
			[WidgetZone.TopBar] = [new WidgetPlacement("Healthy", 0, null)],
			[WidgetZone.LeftSidebar] = [],
			[WidgetZone.RightSidebar] = [],
			[WidgetZone.MainContent] = [],
			[WidgetZone.Footer] = []
		},
		Settings: new LayoutSettings(false, false));

	[TUnit.Core.Test]
	public async Task AWidgetOfAnApplicationThatIsOff_IsLeftOut()
	{
		Register([]);

		var cut = Render<ZoneRenderer>(p => p.Add(c => c.Zone, WidgetZone.TopBar).Add(c => c.Layout, Layout()));

		await Assert.That(cut.Markup).DoesNotContain("healthy-widget");
	}

	[TUnit.Core.Test]
	public async Task AWidgetOfAnApplicationThatIsOn_Renders()
	{
		Register(["scenes"]);

		var cut = Render<ZoneRenderer>(p => p.Add(c => c.Zone, WidgetZone.TopBar).Add(c => c.Layout, Layout()));

		cut.WaitForState(() => cut.Markup.Contains("healthy-widget"));
		await Assert.That(cut.Markup).Contains("healthy-widget");
	}

	/// <summary>An administrator turns the application on in the same session: the widget appears without a reload.</summary>
	[TUnit.Core.Test]
	public async Task AnApplicationTurnedOnLater_ShowsItsWidget()
	{
		List<string> features = [];
		var serverInfo = Register(features);
		var cut = Render<ZoneRenderer>(p => p.Add(c => c.Zone, WidgetZone.TopBar).Add(c => c.Layout, Layout()));
		await Assert.That(cut.Markup).DoesNotContain("healthy-widget");

		features.Add("scenes");
		serverInfo.Refresh();

		cut.WaitForState(() => cut.Markup.Contains("healthy-widget"));
		await Assert.That(cut.Markup).Contains("healthy-widget");
	}
}

public class ZoneRendererUnknownWidgetTests : ZoneRendererTestBase
{
	[TUnit.Core.Test]
	public async Task ZoneRenderer_UnknownWidgetName_RendersNothing()
	{
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
