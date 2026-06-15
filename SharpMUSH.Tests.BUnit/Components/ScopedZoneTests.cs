using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Layout;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Confirms <see cref="ScopedZone"/> loads a scope's layout from <see cref="ILayoutService"/> and
/// renders that zone's placements through the widget registry — the mechanism that lets the example
/// pages compose themselves from admin-customizable widgets.
/// </summary>
public class ScopedZoneTests : BunitContext
{
	public ScopedZoneTests()
	{
		var registry = new WidgetRegistry();
		registry.Register(new WelcomeTextWidgetDescriptor());

		var welcomeConfig = JsonSerializer.SerializeToElement(new { markdown = "# HelloFromZone", showToGuests = true });
		var layout = new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WelcomeText", 0, welcomeConfig)]
			},
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false));

		var layoutService = Substitute.For<ILayoutService>();
		layoutService.GetLayoutAsync(LayoutScopes.Home).Returns(Task.FromResult(layout));

		Services
			.AddMudServices()
			.AddSingleton<IWidgetRegistry>(registry)
			.AddSingleton(layoutService);

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task RendersZonePlacementsFromScopeLayout()
	{
		var cut = Render<ScopedZone>(p => p
			.Add(x => x.Scope, LayoutScopes.Home)
			.Add(x => x.Zone, WidgetZone.MainContent));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("HelloFromZone"))
			{
				throw new InvalidOperationException("layout not rendered yet");
			}
		}, TimeSpan.FromSeconds(5));

		// WelcomeText renders its markdown to HTML — the heading proves the widget resolved and ran.
		await Assert.That(cut.Markup).Contains("HelloFromZone");
		await Assert.That(cut.Markup).Contains("<h1");
	}
}
