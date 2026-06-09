using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// BUnit component tests for <see cref="QuickLinksWidget"/>.
/// </summary>
public abstract class QuickLinksWidgetTestBase : BunitContext
{
	protected QuickLinksWidgetTestBase()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>
	/// Builds a JsonElement from an anonymous object describing QuickLinksConfig.
	/// Shape: { links: [ { label, url, icon?, newTab } ] }
	/// </summary>
	protected static JsonElement BuildConfig(object obj)
	{
		var json = JsonSerializer.Serialize(obj,
			new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
		return JsonDocument.Parse(json).RootElement;
	}
}

public class QuickLinksWidgetNullConfigTests : QuickLinksWidgetTestBase
{
	[TUnit.Core.Test]
	public async Task QuickLinksWidget_NullConfig_RendersNothing()
	{
		var cut = Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, (JsonElement?)null)
			.Add(c => c.Zone, WidgetZone.TopBar.ToString()));

		// No links → empty output
		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}
}

public class QuickLinksWidgetSidebarTests : QuickLinksWidgetTestBase
{
	[TUnit.Core.Test]
	public async Task QuickLinksWidget_LeftSidebarZone_RendersMudNavMenu()
	{
		var config = BuildConfig(new
		{
			Links = new[]
			{
				new { Label = "Home", Url = "/", NewTab = false }
			}
		});

		var cut = Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, config)
			.Add(c => c.Zone, WidgetZone.LeftSidebar.ToString()));

		var nav = cut.FindComponent<MudNavMenu>();
		await Assert.That(nav).IsNotNull();
	}

	[TUnit.Core.Test]
	public async Task QuickLinksWidget_RightSidebarZone_RendersMudNavMenu()
	{
		var config = BuildConfig(new
		{
			Links = new[]
			{
				new { Label = "Wiki", Url = "/wiki", NewTab = true }
			}
		});

		var cut = Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, config)
			.Add(c => c.Zone, WidgetZone.RightSidebar.ToString()));

		var nav2 = cut.FindComponent<MudNavMenu>();
		await Assert.That(nav2).IsNotNull();
	}
}

public class QuickLinksWidgetChipTests : QuickLinksWidgetTestBase
{
	[TUnit.Core.Test]
	public async Task QuickLinksWidget_TopBarZone_RendersMudChips()
	{
		var config = BuildConfig(new
		{
			Links = new[]
			{
				new { Label = "Discord", Url = "https://discord.gg/test", NewTab = true },
				new { Label = "Docs", Url = "/docs", NewTab = false }
			}
		});

		var cut = Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, config)
			.Add(c => c.Zone, WidgetZone.TopBar.ToString()));

		var chips = cut.FindComponents<MudChip<string>>();
		await Assert.That(chips.Count).IsEqualTo(2);
	}

	[TUnit.Core.Test]
	public async Task QuickLinksWidget_FooterZone_RendersMudChips()
	{
		var config = BuildConfig(new
		{
			Links = new[]
			{
				new { Label = "Privacy", Url = "/privacy", NewTab = false }
			}
		});

		var cut = Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, config)
			.Add(c => c.Zone, WidgetZone.Footer.ToString()));

		var chips = cut.FindComponents<MudChip<string>>();
		await Assert.That(chips.Count).IsEqualTo(1);
	}
}
