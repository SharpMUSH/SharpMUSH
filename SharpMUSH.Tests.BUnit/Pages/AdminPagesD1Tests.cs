using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Pages.Admin.Applications;
using SharpMUSH.Client.Pages.Admin.Jobs;
using SharpMUSH.Client.Pages.Admin.Layout;
using SharpMUSH.Client.Pages.Admin.Packages;
using SharpMUSH.Client.Pages.Admin.Roles;
using SharpMUSH.Client.Pages.Admin.Snapshots;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Records every request and answers each with the body a test registered for its path, or 404.</summary>
internal sealed class AdminPagesHandler : HttpMessageHandler
{
	public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
	public List<HttpRequestMessage> Requests { get; } = [];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Requests.Add(request);
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		return Task.FromResult(Bodies.TryGetValue(path, out var body)
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// The Build &amp; manage pages in the D1 frame (README §6.5): every one opens with the kit's plain
/// header naming it, puts its content in kit cards, and keeps its primary actions in the header.
/// </summary>
public class AdminPagesD1Tests : TrackingBunitContext
{
	private readonly AdminPagesHandler _api = new();
	private BunitAuthorizationContext Auth { get; }

	public AdminPagesD1Tests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton<AdminGuestsService>()
			.AddSingleton<AdminAccountsService>()
			.AddSingleton<WikiAssetService>()
			.AddSingleton<WikiService>()
			.AddSingleton<ApplicationRegistryClient>()
			.AddSingleton<DatabaseConversionService>()
			.AddSingleton<PackagesAdminService>()
			.AddSingleton<RoleRegistryClient>()
			.AddSingleton<SitelockService>()
			.AddSingleton<BannedNamesService>()
			.AddSingleton<RestrictionsService>()
			.AddSingleton<AdminConfigService>()
			.AddSingleton<SetupWizardService>()
			.AddSingleton<ILayoutService, LayoutService>()
			.AddSingleton(sp => new AccountAuthService(factory, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []));
		JSInterop.Mode = JSRuntimeMode.Loose;
		Auth = AddAuthorization();
		Auth.SetAuthorized("staff");
	}

	private IRenderedComponent<MudHarness> RenderPage(Type page, Action<RenderTreeBuilderShim>? parameters = null) =>
		Render<MudHarness>(p => p.AddChildContent(builder =>
		{
			builder.OpenComponent(0, page);
			parameters?.Invoke(new RenderTreeBuilderShim(builder));
			builder.CloseComponent();
		}));

	/// <summary>Adds attributes to the page component being opened.</summary>
	internal readonly struct RenderTreeBuilderShim(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
	{
		public void Add(string name, object value) => builder.AddComponentParameter(1, name, value);
	}

	[Test]
	[Arguments(typeof(Players), "AdmPlayersTitle", null)]
	[Arguments(typeof(AdminAccounts), "AdmAccountsTitle", null)]
	[Arguments(typeof(AdminCharacters), "Characters", null)]
	[Arguments(typeof(Moderation), "AdmModerationTitle", null)]
	[Arguments(typeof(AdminServer), "ServerSettings", null)]
	[Arguments(typeof(AdminProfiles), "ProfileHandler", null, ".mud-alert")]
	[Arguments(typeof(AdminMedia), "WkImageLibrary", null)]
	[Arguments(typeof(AdminWiki), "WikiAdmin", null)]
	[Arguments(typeof(AdminWikiAssets), "WkWikiAssets", null)]
	[Arguments(typeof(AdminApplications), "LayApplications", "LayAppsNew")]
	[Arguments(typeof(ImportDatabase), "ImportPennMUSHDatabase", null)]
	[Arguments(typeof(AdminJobs), "JobsTitle", "JobsRefresh")]
	[Arguments(typeof(AdminLayouts), "LayLayouts", null)]
	[Arguments(typeof(AdminPackages), "PkgSoftcodePackages", "PkgBrowse")]
	[Arguments(typeof(AdminPackageBrowse), "PkgBrowseHeading", null)]
	[Arguments(typeof(AdminPackageRemotes), "PkgRemotesHeading", null)]
	[Arguments(typeof(AdminPackageAuthor), "PkgAuthorHeading", null)]
	[Arguments(typeof(AdminPackageReview), "PkgReview", null, ".mud-alert")]
	[Arguments(typeof(QueueDiagnostics), "DiagTitle", null)]
	[Arguments(typeof(AdminRoles), "RolHeading", "RolNewRole")]
	[Arguments(typeof(AdminSnapshots), "SnapshotsTitle", null)]
	[Arguments(typeof(SuggestionManagement), "SuggestionManagement", "AddCategory")]
	[Arguments(typeof(Sitelock), "SitelockRules", null)]
	[Arguments(typeof(BannedNames), "BannedPlayerNames", null)]
	[Arguments(typeof(Restrictions), "CommandAndFunctionRestrictions", null)]
	[Arguments(typeof(ImportConfig), "ImportConfiguration", null)]
	public async Task OpensWithThePlainHeader_AndPutsContentInKitCards(Type page, string title, string? action, string content = ".kit-card, .lay-card")
	{
		var cut = RenderPage(page);

		cut.WaitForAssertion(() => cut.Find(".kit-page-head h1"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-page-head").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".kit-page-head h1").TextContent.Trim()).IsEqualTo(title);
		// A page whose read failed (404 from the fake) says so instead; its cards are covered elsewhere.
		cut.WaitForAssertion(() => cut.Find(content), TimeSpan.FromSeconds(5));
		if (action is not null)
		{
			await Assert.That(cut.Find(".kit-page-actions").TextContent).Contains(action);
		}
	}

	/// <summary>
	/// The config list pages (sitelock, banned names, restrictions) render their add form and their
	/// list through <c>AdminKeyValueList</c>. It used to draw raw <c>&lt;input class="config-input"&gt;</c>
	/// boxes styled by each page's scoped CSS, which never reached into the child component, so they
	/// rendered as unstyled white boxes. The form is now a kit card of MudBlazor fields with a capsule
	/// action, and the list a kit card of rows.
	/// </summary>
	[Test]
	[Arguments(typeof(Sitelock), "api/sitelock", """{"*.bad.example":["!connect"]}""")]
	[Arguments(typeof(BannedNames), "api/bannednames", """["Vader"]""")]
	[Arguments(typeof(Restrictions), "api/restrictions/commands", """{"@nuke":["nobody"]}""")]
	public async Task ConfigListPages_UseKitCardsMudFieldsAndACapsuleAction(Type page, string endpoint, string body)
	{
		_api.Bodies[endpoint] = body;
		var cut = RenderPage(page);

		cut.WaitForAssertion(() => cut.Find(".config-list-name"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-card").Count).IsGreaterThanOrEqualTo(2)
			.Because("the add form and the list each sit in a kit card");
		await Assert.That(cut.FindAll(".config-input, .config-primary-btn, .config-icon-btn, .config-field-card").Count).IsEqualTo(0)
			.Because("the hand-built controls are gone");
		await Assert.That(cut.FindAll("input").Count).IsGreaterThan(0);
		await Assert.That(cut.FindAll("input:not(.mud-input-slot)").Count).IsEqualTo(0)
			.Because("every field is a MudBlazor field styled like the rest of the portal");
		await Assert.That(cut.FindAll("button.kit-capsule.kit-capsule--primary").Count).IsEqualTo(1)
			.Because("the add action is the page's one primary capsule");
		await Assert.That(cut.FindAll("button.mud-icon-button.config-delete[aria-label='Delete']").Count).IsEqualTo(1);
	}

	/// <summary>
	/// The config import page puts the chosen file in a kit card: the preview is a read-only MudBlazor
	/// field rather than a hand-styled textarea, and the import is the page's one primary capsule.
	/// </summary>
	[Test]
	public async Task ImportConfig_ShowsTheChosenFileInAKitCard()
	{
		var cut = RenderPage(typeof(ImportConfig));
		cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
			.UploadFiles(InputFileContent.CreateFromText("mud_name Test\n", "mush.cnf"));

		cut.WaitForAssertion(() => cut.Find("button.kit-capsule--primary"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-card").Count).IsGreaterThanOrEqualTo(2)
			.Because("the drop zone and the chosen file each sit in a kit card");
		await Assert.That(cut.FindAll(".config-field-card, .config-primary-btn, .config-ghost-btn, textarea.config-input").Count).IsEqualTo(0)
			.Because("the hand-built controls are gone");
		await Assert.That(cut.FindAll("button.kit-capsule.kit-capsule--primary").Count).IsEqualTo(1);
		await Assert.That(cut.Find("button.kit-capsule--primary").TextContent).Contains("ImportConfiguration");
		await Assert.That(cut.Find(".kit-card-title").TextContent).IsEqualTo("SelectedFile");

		cut.Find(".mud-expand-panel-header").Click();
		cut.WaitForAssertion(() => cut.Find("textarea.mud-input-slot"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("textarea.mud-input-slot").TextContent + cut.Find("textarea.mud-input-slot").GetAttribute("value"))
			.Contains("mud_name Test");
	}

	/// <summary>
	/// The command and function lists are switched with the kit's chips rather than a MudBlazor tab
	/// strip, and each chip still loads its own list.
	/// </summary>
	[Test]
	public async Task Restrictions_SwitchesItsTwoListsWithKitChips()
	{
		_api.Bodies["api/restrictions/commands"] = """{"@nuke":["nobody"]}""";
		_api.Bodies["api/restrictions/functions"] = """{"pemit":["nobody"]}""";
		var cut = RenderPage(typeof(Restrictions));

		cut.WaitForAssertion(() => cut.Find(".config-list-name"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".mud-tabs").Count).IsEqualTo(0);
		var chips = cut.FindAll(".kit-chips .kit-chip");
		await Assert.That(chips.Count).IsEqualTo(2);
		await Assert.That(cut.Find(".kit-chip--on").TextContent.Trim()).IsEqualTo("CommandRestrictions");
		await Assert.That(cut.Find(".config-list-name").TextContent).IsEqualTo("@nuke");

		chips[1].Click();

		cut.WaitForAssertion(() =>
		{
			var name = cut.Find(".config-list-name").TextContent;
			if (name != "pemit") throw new InvalidOperationException($"list shows '{name}'");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-chip--on").TextContent.Trim()).IsEqualTo("FunctionRestrictions");
		await Assert.That(cut.Find(".kit-card-title").TextContent).IsEqualTo("AddFunctionRestriction");
	}

	/// <summary>The README buttons were labelled with a literal English "README" in every locale.</summary>
	[Test]
	[Arguments("Pages/Admin/Packages/AdminPackageBrowse.razor")]
	[Arguments("Pages/Admin/Packages/AdminPackageRemotes.razor")]
	public async Task TheReadmeButtons_AreLocalised(string file)
	{
		var source = File.ReadAllText(Path.Join(ClientSource.RazorRoot, file));
		await Assert.That(source).DoesNotContain("\"README\"");
		await Assert.That(source).Contains("Loc[\"PkgShowReadme\"");
	}

	[Test]
	public async Task PlayerDetail_NamesThePlayerInTheHeader()
	{
		var cut = RenderPage(typeof(PlayerDetail), p => p.Add(nameof(PlayerDetail.Id), 42));

		await Assert.That(cut.Find(".kit-page-head h1").TextContent.Trim()).IsEqualTo("AdmPlayerNumber(42)");
		await Assert.That(cut.FindAll(".kit-card").Count).IsGreaterThan(0);
	}

	[Test]
	public async Task Dashboard_OffersOnlyTheToolsTheViewerMayOpen()
	{
		Auth.SetPolicies("players.view", "queue.inspect.own");
		var cut = RenderPage(typeof(Dashboard));

		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card[href='/admin/players']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-page-head h1").TextContent.Trim()).IsEqualTo("AdmDashboardTitle");
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/characters']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/diagnostics']").Count).IsEqualTo(1)
			.Because("queue.inspect.own alone opens the diagnostics page, as in the section sidebar");
		foreach (var gated in new[] { "/admin/accounts", "/admin/config", "/admin/roles", "/admin/moderation", "/admin/profiles",
			"/admin/suggestions", "/admin/wiki", "/admin/media", "/admin/applications", "/admin/packages", "/admin/layout",
			"/admin/server", "/admin/database/import" })
		{
			await Assert.That(cut.FindAll($"a.adm-dash-card[href='{gated}']").Count).IsEqualTo(0).Because(gated);
		}
	}

	[Test]
	public async Task Dashboard_OffersAccountsToWizards()
	{
		Auth.SetPolicies("players.view");
		Auth.SetRoles("Wizard");
		var cut = RenderPage(typeof(Dashboard));

		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card[href='/admin/accounts']"), TimeSpan.FromSeconds(5));
	}

	/// <summary>
	/// An administrator who left the first-run wizard after the claim is pointed back to it; nobody else
	/// is, and nobody is once it is finished.
	/// </summary>
	[Test]
	[Arguments("server.admin", true, true)]
	[Arguments("server.admin", false, false)]
	[Arguments("players.view", true, false)]
	public async Task Dashboard_PointsTheAdministratorBackToAnUnfinishedSetup(string policy, bool pending, bool shown)
	{
		Auth.SetPolicies("players.view", policy);
		_api.Bodies["api/setup/wizard"] = $$"""{"pending":{{(pending ? "true" : "false")}},"applications":[]}""";
		var cut = RenderPage(typeof(Dashboard));

		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("a.adm-dash-setup-link[href='/setup']").Count).IsEqualTo(shown ? 1 : 0);
	}

	[Test]
	public async Task Jobs_HidesTheCreateCard_FromAViewerWhoMayOnlyManageAll()
	{
		Auth.SetPolicies("jobs.manage");
		_api.Bodies["api/recurring-jobs"] = "[]";
		var cut = RenderPage(typeof(AdminJobs));

		cut.WaitForAssertion(() => cut.Find(".kit-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).DoesNotContain("JobsCreate");
		await Assert.That(cut.Markup).Contains("JobsAll");
	}

	[Test]
	public async Task Snapshots_HidesTheCaptureCard_WithoutTheCapturePermission()
	{
		var cut = RenderPage(typeof(AdminSnapshots));

		cut.WaitForAssertion(() => cut.Find(".kit-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).DoesNotContain("SnapshotsCapture");
	}

	[Test]
	public async Task Snapshots_ShowsTheCaptureCard_WithTheCapturePermission()
	{
		Auth.SetPolicies("snapshots.capture");
		var cut = RenderPage(typeof(AdminSnapshots));

		cut.WaitForAssertion(() => cut.Find(".kit-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).Contains("SnapshotsCapture");
	}

	[Test]
	public async Task Packages_CountsTheConfiguredRemotes()
	{
		_api.Bodies["api/packages"] = "[]";
		_api.Bodies["api/packages/remotes"] =
			"""[{"name":"official","url":"https://example.com/a.git","trust":0,"branch":null},{"name":"mine","url":"https://example.com/b.git","trust":2,"branch":null}]""";
		var cut = RenderPage(typeof(AdminPackages));

		cut.WaitForAssertion(() =>
		{
			var remotes = cut.Find(".adm-stat[data-stat='remotes'] .adm-stat-value").TextContent.Trim();
			if (remotes != "2") throw new InvalidOperationException($"remotes tile reads '{remotes}'");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task Suggestions_EscapesTheWordWhenDeletingIt()
	{
		_api.Bodies["api/suggestion"] = """{"categories":{"slang":["a b?c#d"]}}""";
		var cut = RenderPage(typeof(SuggestionManagement));

		cut.WaitForAssertion(() => cut.Find(".mud-expand-panel-header"), TimeSpan.FromSeconds(5));
		cut.Find(".mud-expand-panel-header").Click();
		cut.WaitForAssertion(() => cut.Find(".mud-chip-close-button"), TimeSpan.FromSeconds(5));
		cut.Find(".mud-chip-close-button").Click();

		cut.WaitForAssertion(() =>
		{
			if (!_api.Requests.Any(r => r.Method == HttpMethod.Delete))
				throw new InvalidOperationException("no delete sent yet");
		}, TimeSpan.FromSeconds(5));
		var delete = _api.Requests.Single(r => r.Method == HttpMethod.Delete);
		await Assert.That(delete.RequestUri!.AbsolutePath).IsEqualTo("/api/suggestion/slang/a%20b%3Fc%23d");
	}

	[Test]
	public async Task Suggestions_KeepsEachCategorysNewWordApart()
	{
		_api.Bodies["api/suggestion"] = """{"categories":{"one":["x"],"two":["y"]}}""";
		var cut = RenderPage(typeof(SuggestionManagement));

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mud-expand-panel-header").Count != 2) throw new InvalidOperationException("categories not loaded");
		}, TimeSpan.FromSeconds(5));
		foreach (var header in cut.FindAll(".mud-expand-panel-header"))
		{
			header.Click();
		}
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("input.mud-input-slot").Count < 2) throw new InvalidOperationException("panels not open");
		}, TimeSpan.FromSeconds(5));

		cut.FindAll("input.mud-input-slot")[0].Change("typed-in-one");

		await Assert.That(cut.FindAll("input.mud-input-slot")[1].GetAttribute("value") ?? string.Empty).IsEqualTo(string.Empty);
	}
}
