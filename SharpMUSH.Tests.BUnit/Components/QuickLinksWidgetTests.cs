using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
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
	/// <summary>The empty-state "Configure Quick Links" prompt is gated by AuthorizeView, so tests need an auth context.</summary>
	protected BunitAuthorizationContext Auth { get; }

	protected QuickLinksWidgetTestBase()
	{
		Services.AddMudServices();
		Auth = AddAuthorization();
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
	public async Task QuickLinksWidget_NullConfig_NonAdmin_RendersNothing()
	{
		// Default auth context: not authorized → the admin-only configure prompt does not render.
		var cut = Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, (JsonElement?)null)
			.Add(c => c.Zone, WidgetZone.TopBar.ToString()));

		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}

	[TUnit.Core.Test]
	public async Task QuickLinksWidget_NullConfig_Admin_ShowsConfigurePrompt()
	{
		Auth.SetAuthorized("Wizard");
		Auth.SetPolicies("layout.admin");

		var cut = Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, (JsonElement?)null)
			.Add(c => c.Zone, WidgetZone.TopBar.ToString()));

		await Assert.That(cut.Markup).Contains("Configure Quick Links");
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
