using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Tests.BUnit.Resources;

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
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
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

		await Assert.That(cut.Markup).Contains("WidConfigureQuickLinks");
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

public class QuickLinksWidgetIconTests : QuickLinksWidgetTestBase
{
	private IRenderedComponent<QuickLinksWidget> RenderIcon(string? icon, WidgetZone zone) =>
		Render<QuickLinksWidget>(p => p
			.Add(c => c.Config, BuildConfig(new { Links = new[] { new { Label = "Rules", Url = "/wiki/rules", Icon = icon } } }))
			.Add(c => c.Zone, zone.ToString()));

	[TUnit.Core.Test]
	[Arguments(WidgetZone.TopBar)]
	[Arguments(WidgetZone.LeftSidebar)]
	public async Task AnUploadedPicture_IsShownAsTheIcon(WidgetZone zone)
	{
		var cut = RenderIcon("/api/wiki-assets/abc/rules.svg", zone);

		await Assert.That(cut.Find("img.ql-icon").GetAttribute("src")).IsEqualTo("/api/wiki-assets/abc/rules.svg");
		await Assert.That(cut.Find("img.ql-icon").GetAttribute("alt")).IsEqualTo(string.Empty).Because("the label names the link");
	}

	[TUnit.Core.Test]
	public async Task InlineSvgMarkup_IsDrawnAsBefore()
	{
		var cut = RenderIcon("<path d=\"M1 1h2\"/>", WidgetZone.TopBar);

		await Assert.That(cut.FindAll("img.ql-icon").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains("M1 1h2");
	}

	[TUnit.Core.Test]
	[Arguments(null)]
	[Arguments("javascript:alert(1)")]
	[Arguments("http://example.com/plain.png")]
	public async Task NoLoadablePicture_ShowsTheGenericLinkIcon(string? icon)
	{
		var cut = RenderIcon(icon, WidgetZone.TopBar);

		await Assert.That(cut.FindAll("img.ql-icon").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains(Icons.Material.Outlined.Link);
	}
}
