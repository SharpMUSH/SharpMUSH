using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The Build &amp; manage section (README §3): its sidebar lists the overview and what the viewer may
/// use, by group, then the applications placed there under Apps; the config tree offers a way back
/// to it; and an application page joins its section's sidebar (Build &amp; manage, or a novel section's
/// own list of apps).
/// </summary>
public class BuildSectionTests : TrackingBunitContext
{
	private sealed class AppsHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			const string apps = """
				[{"slug":"bbs","displayName":"Bulletin Board","icon":null,"kind":"Page","schemaUrl":"x","dataUrl":null,"submitRoute":null,
				  "minimumRole":"Guest","navPlacement":"Manage","zones":[],"order":1,"owningPackage":null},
				 {"slug":"crafting","displayName":"Crafting","icon":null,"kind":"Page","schemaUrl":"x","dataUrl":null,"submitRoute":null,
				  "minimumRole":"Guest","navPlacement":"Workshop","zones":[],"order":2,"owningPackage":null},
				 {"slug":"recipes","displayName":"Recipes","icon":null,"kind":"Page","schemaUrl":"x","dataUrl":null,"submitRoute":null,
				  "minimumRole":"Guest","navPlacement":"Workshop","zones":[],"order":3,"owningPackage":null}]
				""";
			return Task.FromResult(request.RequestUri!.AbsolutePath == "/api/applications"
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(apps, Encoding.UTF8, "application/json") }
				: new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}

	private readonly BunitAuthorizationContext _auth;

	public BuildSectionTests()
	{
		var client = Track(new HttpClient(new AppsHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton<SidebarCollapseService>()
			.AddSingleton(new AdminConfigService(NullLogger<AdminConfigService>.Instance, factory))
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
		_auth = AddAuthorization();
		_auth.SetAuthorized("staff");
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	[Test]
	public async Task TheSidebar_ListsWhatTheViewerMayUse_ThenTheApps()
	{
		_auth.SetPolicies("softcode.use", "config.admin");
		Nav.NavigateTo("/admin/config/chat");
		var cut = Render<BuildSidebar>();
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/apps/bbs']"), TimeSpan.FromSeconds(5));

		var rows = cut.FindAll("a.kit-row").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(rows).IsEquivalentTo(new[] { "/admin", "/softcode", "/admin/suggestions", "/admin/config", "/apps/bbs" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("the overview, then only the viewer's gates in group order, then the section's apps; Workshop apps are another section");
		await Assert.That(cut.Find("a.kit-row[href='/admin/config']").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.Find(".kit-side-title").TextContent).IsEqualTo("Build & manage");
	}

	/// <summary>
	/// The sidebar lists every staff page the overview does, so each page marks its own row; a page under
	/// one (a character's detail) marks its list, and the one page left out (the design kit) marks the
	/// overview, so the reader never loses their place.
	/// </summary>
	[Test]
	[Arguments("/admin/guests", "/admin/guests")]
	[Arguments("/admin/characters/7", "/admin/characters")]
	[Arguments("/admin/kit", "/admin")]
	[Arguments("/admin", "/admin")]
	[Arguments("/admin/roles", "/admin/roles")]
	[Arguments("/admin/packages/browse", "/admin/packages")]
	public async Task AnAdminPageTheSidebarDoesNotList_MarksTheDashboard(string path, string current)
	{
		_auth.SetPolicies("players.view", "roles.admin", "packages.admin");
		Nav.NavigateTo(path);
		var cut = Render<BuildSidebar>();
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/admin']"), TimeSpan.FromSeconds(5));

		var marked = cut.FindAll("a.kit-row[aria-current='page']").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(marked).IsEquivalentTo(new[] { current });
	}

	[Test]
	public async Task TheConfigTree_LeadsBackToTheOverview()
	{
		_auth.SetPolicies("config.admin", "roles.admin");
		Nav.NavigateTo("/admin/config");
		var staff = Render<ConfigSidebar>();
		staff.WaitForAssertion(() => staff.Find("a.config-side-back"), TimeSpan.FromSeconds(5));
		await Assert.That(staff.Find("a.config-side-back").GetAttribute("href")).IsEqualTo("/admin")
			.Because("the way back is the overview, not whichever tool happens to come first");
	}

	[Test]
	public async Task AManageApplication_ShowsTheBuildSidebar()
	{
		_auth.SetPolicies("roles.admin");
		var app = new PortalApplication("bbs", "Bulletin Board", null, "Page", "x", null, null, "Guest", "Manage", [], 1);
		var cut = Render<PageSidebarHost>(h => h.AddChildContent<AppSectionFrame>(p => p.Add(x => x.App, app)));
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .build-side a.kit-row[href='/apps/bbs']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar").GetAttribute("data-section")).IsEqualTo("build");
	}

	[Test]
	public async Task ANovelSectionsApplication_ShowsThatSectionsApps()
	{
		var app = new PortalApplication("crafting", "Crafting", null, "Page", "x", null, null, "Guest", "Workshop", [], 2);
		Nav.NavigateTo("/apps/crafting");
		var cut = Render<PageSidebarHost>(h => h.AddChildContent<AppSectionFrame>(p => p.Add(x => x.App, app)));
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .app-section-side a.kit-row[href='/apps/recipes']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".test-pagebar .kit-side-title").TextContent).IsEqualTo("Workshop");
		await Assert.That(cut.Find("a.kit-row[href='/apps/crafting']").GetAttribute("aria-current")).IsEqualTo("page");
	}

	[Test]
	public async Task APlayOrWorldApplication_HasNoSectionSidebar()
	{
		var app = new PortalApplication("map", "Map", null, "Page", "x", null, null, "Guest", "World", [], 3);
		var cut = Render<PageSidebarHost>(h => h.AddChildContent<AppSectionFrame>(p => p.Add(x => x.App, app)
			.AddChildContent("<p id=\"body\">x</p>")));
		await Assert.That(cut.FindAll(".test-pagebar .kit-pagebar").Count).IsEqualTo(0);
		await Assert.That(cut.Find("#body")).IsNotNull();
	}
}
