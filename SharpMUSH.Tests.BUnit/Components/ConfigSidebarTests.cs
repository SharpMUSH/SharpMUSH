using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Models.Configuration;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The D1 configuration sidebar: a tree of the six groups with the current section's group open,
/// per-section counts when the schema is known, and Maintenance pinned at the bottom.
/// </summary>
public class ConfigSidebarTests : TrackingBunitContext
{
	private readonly BunitNavigationManager _nav;

	public ConfigSidebarTests()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(Track(new HttpClient(new HttpClientHandler()) { BaseAddress = new Uri("https://localhost:8081/") }));
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<AdminConfigService>()
			.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
		AddAuthorization();
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	[Test]
	public async Task ListsEveryGroupAndSection()
	{
		_nav.NavigateTo("/admin/config");
		var cut = Render<ConfigSidebar>();
		await Assert.That(cut.FindAll("details.config-side-group").Count).IsEqualTo(6);
		await Assert.That(cut.FindAll("details.config-side-group a.kit-row").Count)
			.IsEqualTo(ConfigSections.Groups.Sum(g => g.Sections.Count));
		await Assert.That(cut.Find("a.kit-row[href='/admin/config/wiki']")).IsNotNull()
			.Because("Wiki is a Content section; the old nav listed it while the home card did not count it");
	}

	[Test]
	public async Task TheCurrentSectionsGroupIsOpen_AndTheRowIsCurrent()
	{
		_nav.NavigateTo("/admin/config/chat");
		var cut = Render<ConfigSidebar>();
		var content = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/chat']") is not null);
		await Assert.That(content.HasAttribute("open")).IsTrue();
		await Assert.That(cut.Find("a[href='/admin/config/chat']").GetAttribute("aria-current")).IsEqualTo("page");
		var server = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/net']") is not null);
		await Assert.That(server.HasAttribute("open")).IsFalse();
	}

	[Test]
	public async Task TheSitelockRulesAlias_IsTheSitelockSection()
	{
		_nav.NavigateTo("/admin/config/sitelockrules");
		var cut = Render<ConfigSidebar>();
		await Assert.That(cut.Find("a[href='/admin/config/sitelock']").GetAttribute("aria-current")).IsEqualTo("page");
	}

	[Test]
	public async Task NavigatingToAnotherSection_OpensItsGroup()
	{
		_nav.NavigateTo("/admin/config/chat");
		var cut = Render<ConfigSidebar>();
		_nav.NavigateTo("/admin/config/net");
		cut.WaitForState(() => cut.FindAll("details.config-side-group")
			.Any(d => d.HasAttribute("open") && d.QuerySelector("a[href='/admin/config/net']") is not null));
		await Assert.That(cut.Find("a[href='/admin/config/net']").GetAttribute("aria-current")).IsEqualTo("page");
	}

	[Test]
	public async Task ClickingASummary_TogglesItsGroup_AndAriaExpanded()
	{
		_nav.NavigateTo("/admin/config/chat");
		var cut = Render<ConfigSidebar>();
		var content = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/chat']") is not null);
		await Assert.That(content.QuerySelector("summary")!.GetAttribute("aria-expanded")).IsEqualTo("true");

		await content.QuerySelector("summary")!.ClickAsync();
		content = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/chat']") is not null);
		await Assert.That(content.HasAttribute("open")).IsFalse();
		await Assert.That(content.QuerySelector("summary")!.GetAttribute("aria-expanded")).IsEqualTo("false");

		await content.QuerySelector("summary")!.ClickAsync();
		content = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/chat']") is not null);
		await Assert.That(content.HasAttribute("open")).IsTrue();
	}

	[Test]
	public async Task SectionCounts_RenderAsDimCounts()
	{
		_nav.NavigateTo("/admin/config/chat");
		var cut = Render<ConfigSidebar>(p => p.Add(x => x.SectionCounts, new Dictionary<string, int> { ["Chat"] = 7 }));
		await Assert.That(cut.Find("a[href='/admin/config/chat'] .kit-row-count").TextContent).IsEqualTo("7");
		await Assert.That(cut.FindAll("a[href='/admin/config/net'] .kit-row-count").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Collapsed_ShowsOnlyGroupAndMaintenanceIcons()
	{
		_nav.NavigateTo("/admin/config");
		var cut = Render<ConfigSidebar>(p => p.Add(x => x.Collapsed, true));
		await Assert.That(cut.FindAll("details").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-row--collapsed").Count).IsEqualTo(6 + 2);
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
	}

	[Test]
	public async Task MaintenanceIsPinnedAtTheBottom_WithImportAndExport()
	{
		_nav.NavigateTo("/admin/config");
		var cut = Render<ConfigSidebar>();
		await Assert.That(cut.Find(".config-side-maint a[href='/admin/config/import']")).IsNotNull();
		await Assert.That(cut.Find(".config-side-maint button.kit-row").TextContent).Contains("Export");
	}
}
