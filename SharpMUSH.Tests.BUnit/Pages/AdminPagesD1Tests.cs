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
			.AddSingleton<AdminCharactersService>()
			.AddSingleton<AdminAuditService>()
			.AddSingleton<AdminBansService>()
			.AddSingleton<AdminServerService>()
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
	[Arguments(typeof(AdminGuests), "AdmGuestsTitle", null)]
	[Arguments(typeof(AdminAccounts), "AdmAccountsTitle", null)]
	[Arguments(typeof(AdminCharacters), "Characters", null)]
	[Arguments(typeof(AuditLog), "AdmAuditTitle", null)]
	[Arguments(typeof(Moderation), "AdmModerationTitle", null)]
	[Arguments(typeof(AdminServer), "AdmServerTitle", null, ".adm-stat, .mud-alert")]
	[Arguments(typeof(AdminProfiles), "ProfileHandler", null, ".mud-alert")]
	[Arguments(typeof(AdminMedia), "WkImageLibrary", null)]
	[Arguments(typeof(AdminWiki), "WikiAdmin", null)]
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

		await cut.Find(".mud-expand-panel-header").ClickAsync();
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

		await chips[1].ClickAsync();

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

		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card[href='/admin/guests']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-page-head h1").TextContent.Trim()).IsEqualTo("AdmOverview");
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/diagnostics']").Count).IsEqualTo(0)
			.Because("queue.inspect.own is every player's; the diagnostics page is for queue.inspect");
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/characters']").Count).IsEqualTo(1);
		foreach (var gated in new[] { "/admin/snapshots", "/admin/jobs", "/admin/accounts", "/admin/audit", "/admin/config", "/admin/roles", "/admin/moderation", "/admin/profiles",
			"/admin/suggestions", "/admin/messages", "/admin/wiki", "/admin/media", "/admin/applications", "/admin/packages", "/admin/layout",
			"/admin/server", "/admin/import" })
		{
			await Assert.That(cut.FindAll($"a.adm-dash-card[href='{gated}']").Count).IsEqualTo(0).Because(gated);
		}
	}

	/// <summary>A moderator (Royalty's scopes) is offered characters, moderation and the audit log, not configuration.</summary>
	[Test]
	public async Task Dashboard_OffersModerationToModerators()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		var cut = RenderPage(typeof(Dashboard));

		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card[href='/admin/moderation']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/characters']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/audit']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/config']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("a.adm-dash-card[href='/admin/server']").Count).IsEqualTo(0);
	}

	/// <summary>
	/// Every card opens a page the portal has, so none is a dead link. The server card is offered to
	/// whoever holds <c>server.admin</c>.
	/// </summary>
	[Test]
	public async Task Dashboard_EveryCardOpensARoutedPage()
	{
		Auth.SetPolicies("players.view", "players.moderate", "server.admin", "config.admin", "roles.admin",
			"wiki.admin", "media.admin", "applications.admin", "packages.admin", "layout.admin", "queue.inspect");
		Auth.SetRoles("God");
		var cut = RenderPage(typeof(Dashboard));
		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card[href='/admin/server']"), TimeSpan.FromSeconds(5));

		var routes = Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories)
			.Select(File.ReadAllText)
			.SelectMany(source => System.Text.RegularExpressions.Regex.Matches(source, "^@page \"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Multiline))
			.Select(match => match.Groups[1].Value)
			.ToHashSet(StringComparer.Ordinal);
		var cards = cut.FindAll("a.adm-dash-card").Select(card => card.GetAttribute("href")!).ToList();
		await Assert.That(cards.Count).IsGreaterThan(10);
		foreach (var href in cards)
			await Assert.That(routes).Contains(href).Because(href);
	}

	private static string ServerStatusJson(bool ready, int queued = 3) => System.Text.Json.JsonSerializer.Serialize(
		new SharpMUSH.Library.API.AdminServerStatus("1.2.3.4", "abc123", DateTimeOffset.UtcNow.AddDays(-2).AddHours(-5),
			Connections: 7, Players: 5, QueuedTasks: queued, QueueLimit: 10, Ready: ready,
			Pending: ready ? [] : ["output-bridge"],
			Streams: [new("SHARPMUSH-CS", 100, 1000)], BusBacklog: 4, BusReadAt: true,
			Storage: new(2048, 4096, 1 << 20, 1 << 30), LastBackup: null, BackupSupported: true),
		System.Text.Json.JsonSerializerOptions.Web);

	private static string Stat(IRenderedComponent<MudHarness> cut, string key) =>
		cut.Find($"[data-stat='{key}'] .adm-stat-value").TextContent.Trim();

	[Test]
	public async Task ServerPage_ShowsTheServersFigures()
	{
		_api.Bodies["api/admin/server/status"] = ServerStatusJson(ready: true);
		var cut = RenderPage(typeof(AdminServer));
		cut.WaitForAssertion(() => cut.Find("[data-stat='ready']"), TimeSpan.FromSeconds(5));

		await Assert.That(Stat(cut, "ready")).IsEqualTo("AdmServerReady");
		await Assert.That(Stat(cut, "uptime")).IsEqualTo("AdmServerUptimeDays(2, 5)");
		await Assert.That(Stat(cut, "players")).IsEqualTo("5");
		await Assert.That(cut.Find("[data-stat='players'] .adm-stat-sub").TextContent).IsEqualTo("AdmServerConnections(7)");
		await Assert.That(Stat(cut, "queue")).IsEqualTo("3");
		await Assert.That(Stat(cut, "bus")).IsEqualTo("4");
		await Assert.That(Stat(cut, "version")).IsEqualTo("1.2.3.4");
		await Assert.That(Stat(cut, "backup")).IsEqualTo("AdmServerBackupNone");
		await Assert.That(cut.FindAll("[data-stat='ready'].adm-stat--warn").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("[data-stat='backup'].adm-stat--warn").Count).IsEqualTo(1)
			.Because("a provider that takes copies and has none on disk needs attention");
	}

	[Test]
	public async Task ServerPage_FlagsWhatNeedsAttention()
	{
		_api.Bodies["api/admin/server/status"] = ServerStatusJson(ready: false, queued: 9);
		var cut = RenderPage(typeof(AdminServer));
		cut.WaitForAssertion(() => cut.Find("[data-stat='ready']"), TimeSpan.FromSeconds(5));

		await Assert.That(Stat(cut, "ready")).IsEqualTo("AdmServerNotReady");
		await Assert.That(cut.Find("[data-stat='ready'] .adm-stat-sub").TextContent).IsEqualTo("AdmServerWaitingOn(output-bridge)");
		await Assert.That(cut.FindAll("[data-stat='ready'].adm-stat--warn").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("[data-stat='queue'].adm-stat--warn").Count).IsEqualTo(1)
			.Because("9 of a limit of 10 is past 80%");
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
	/// The overview's cards are the section sidebar's entries, under the same group headings in the
	/// same order, so the two can never offer different pages.
	/// </summary>
	[Test]
	public async Task Dashboard_GroupsItsCardsAsTheSidebarDoes()
	{
		Auth.SetPolicies("softcode.use", "players.view", "players.moderate", "wiki.admin", "config.admin", "server.admin");
		var cut = RenderPage(typeof(Dashboard));
		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card[href='/admin/import']"), TimeSpan.FromSeconds(5));

		var groups = cut.FindAll(".adm-dash-group").Select(g => g.GetAttribute("aria-label")).ToList();
		await Assert.That(groups).IsEquivalentTo(new[] { "NavSectionBuild", "AdmGroupPeople", "AdmGroupModeration", "AdmGroupContent", "AdmGroupPortal", "AdmGroupServer" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		var cards = cut.FindAll("a.adm-dash-card").Select(c => c.GetAttribute("href")).ToList();
		await Assert.That(cards).IsEquivalentTo(new[]
		{
			"/softcode", "/admin/characters", "/admin/guests", "/admin/moderation", "/admin/audit",
			"/admin/wiki", "/admin/suggestions", "/admin/messages", "/admin/profiles", "/admin/server", "/admin/config", "/admin/import",
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
		_api.Bodies["api/setup/wizard"] = $$"""{"pending":{{(pending ? "true" : "false")}},"handlers":[],"packages":[]}""";
		var cut = RenderPage(typeof(Dashboard));

		cut.WaitForAssertion(() => cut.Find("a.adm-dash-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("a.adm-dash-setup-link[href='/setup']").Count).IsEqualTo(shown ? 1 : 0);
	}

	/// <summary>
	/// The wizard opens the import page as <c>?setup=1</c>, and the page hands back to the wizard from its
	/// header. A bool query parameter threw on that "1" and took the page down.
	/// </summary>
	[Test]
	[Arguments("/admin/import?setup=1", true)]
	[Arguments("/admin/import", false)]
	public async Task ImportDatabase_FromTheWizard_OffersTheWayBack(string address, bool offered)
	{
		Auth.SetPolicies("server.admin");
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo(address);
		var cut = RenderPage(typeof(ImportDatabase));

		cut.WaitForAssertion(() => cut.Find(".dbimport-page"), TimeSpan.FromSeconds(5));
		var back = cut.FindAll("a.dbimport-continue-setup");
		await Assert.That(back.Count).IsEqualTo(offered ? 1 : 0);
		if (offered)
			await Assert.That(back[0].GetAttribute("href")).IsEqualTo("/setup?step=handlers");
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

	/// <summary>With no object identity, Load and Capture sent the request anyway and showed "the snapshot
	/// request failed. Reload and try again"; they stay off until an identity is typed.</summary>
	[Test]
	public async Task Snapshots_LoadAndCapture_WaitForAnObjectIdentity()
	{
		Auth.SetPolicies("snapshots.capture");
		var cut = RenderPage(typeof(AdminSnapshots));
		cut.WaitForAssertion(() => cut.Find(".snapshots-lookup input"), TimeSpan.FromSeconds(5));

		bool Disabled(string label) => cut.FindAll("button").Single(b => b.TextContent.Trim() == label).HasAttribute("disabled");
		await Assert.That(Disabled("SnapshotsLoad")).IsTrue();
		await Assert.That(Disabled("SnapshotsCapture")).IsTrue();

		await cut.Find(".snapshots-lookup input").InputAsync("#12:1700000000000");
		await Assert.That(Disabled("SnapshotsLoad")).IsFalse();
		await Assert.That(Disabled("SnapshotsCapture")).IsFalse();
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

	/// <summary>
	/// A package that sets a configuration option lists it with its value now and after, and the apply waits for the
	/// admin to confirm the change.
	/// </summary>
	[Test]
	public async Task PackageReview_ListsTheOptionsItSets_AndWaitsForTheirConfirmation()
	{
		var changeset = new SharpMUSH.Library.Models.Packages.PackageChangeset("messages", null, "1.0.0",
			SharpMUSH.Library.Models.Packages.PackageRevisionKind.Install, [], [], [], [], [], [],
			Settings: [new("messages_object", SharpMUSH.Library.Models.Packages.PackageSettingAction.Set, null, "#12",
				"The object this install creates.")]);
		_api.Bodies["api/packages/plan"] = System.Text.Json.JsonSerializer.Serialize(
			new SharpMUSH.Library.API.PlanResponse("messages", "1.0.0", "abc", changeset, [], [], []),
			new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
		Services.GetRequiredService<NavigationManager>().NavigateTo("admin/packages/review?remote=official&path=messages");

		var cut = RenderPage(typeof(AdminPackageReview));

		cut.WaitForAssertion(() => cut.Find(".pkg-settings"), TimeSpan.FromSeconds(5));
		var row = cut.Find(".pkg-settings tbody tr").TextContent;
		await Assert.That(row).Contains("messages_object");
		await Assert.That(row).Contains("PkgSettingNone");
		await Assert.That(row).Contains("#12");
		bool ApplyDisabled() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "PkgApply").HasAttribute("disabled");
		await Assert.That(ApplyDisabled()).IsTrue();

		await cut.Find("input#pkg-settings-confirm, #pkg-settings-confirm input").ChangeAsync(true);
		cut.WaitForAssertion(() =>
		{
			if (ApplyDisabled()) throw new InvalidOperationException("apply still waits");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task Suggestions_EscapesTheWordWhenDeletingIt()
	{
		_api.Bodies["api/suggestion"] = """{"categories":{"slang":["a b?c#d"]}}""";
		var cut = RenderPage(typeof(SuggestionManagement));

		cut.WaitForAssertion(() => cut.Find(".mud-expand-panel-header"), TimeSpan.FromSeconds(5));
		await cut.Find(".mud-expand-panel-header").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".mud-chip-close-button"), TimeSpan.FromSeconds(5));
		await cut.Find(".mud-chip-close-button").ClickAsync();

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
			await header.ClickAsync();
		}
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("input.mud-input-slot").Count < 2) throw new InvalidOperationException("panels not open");
		}, TimeSpan.FromSeconds(5));

		await cut.FindAll("input.mud-input-slot")[0].ChangeAsync("typed-in-one");

		await Assert.That(cut.FindAll("input.mud-input-slot")[1].GetAttribute("value") ?? string.Empty).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// The applications page always has one selected while any are registered: the first on load, and
	/// whichever card is clicked after that (<c>AdminRecordList</c> with <c>AdminReselect.First</c>).
	/// </summary>
	[Test]
	public async Task Applications_SelectsTheFirstOnLoad_AndTheClickedCardAfter()
	{
		_api.Bodies["api/applications"] = """
			[{"slug":"alpha","displayName":"Alpha","kind":"Page","schemaUrl":"","minimumRole":"Player","zones":[],"order":0},
			 {"slug":"beta","displayName":"Beta","kind":"Widget","schemaUrl":"","minimumRole":"Player","zones":[],"order":1}]
			""";
		var cut = RenderPage(typeof(AdminApplications));

		cut.WaitForAssertion(() => cut.Find(".aa-detail-name"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".aa-detail-name").TextContent).IsEqualTo("Alpha");
		await Assert.That(cut.Find(".aa-card--on .aa-card-name").TextContent).IsEqualTo("Alpha");

		await cut.FindAll(".aa-card")[1].ClickAsync();

		await Assert.That(cut.Find(".aa-detail-name").TextContent).IsEqualTo("Beta");
		await Assert.That(cut.Find(".aa-card--on .aa-card-name").TextContent).IsEqualTo("Beta");
	}

	/// <summary>
	/// The roles page starts with nothing selected; clicking a role selects it and fills the editor
	/// from it (<c>AdminRecordList</c> with <c>AdminReselect.Nothing</c> and a selection callback).
	/// </summary>
	[Test]
	public async Task Roles_SelectsNothingOnLoad_AndFillsTheEditorFromTheClickedRole()
	{
		_api.Bodies["api/roles"] = """
			[{"slug":"builder","name":"Builder","category":"Staff","color":"#123456","priority":10,"isSystem":false,"permissions":{},"createdAt":0,"updatedAt":0},
			 {"slug":"admin","name":"Admin","category":"System","color":"#654321","priority":90,"isSystem":true,"permissions":{},"createdAt":0,"updatedAt":0}]
			""";
		var cut = RenderPage(typeof(AdminRoles));

		cut.WaitForAssertion(() => cut.Find(".ra-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".ra-card--on").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".ra-empty-detail").Count).IsEqualTo(1);

		// Sorted by priority, highest first: Admin, then Builder.
		await cut.FindAll(".ra-card")[1].ClickAsync();

		await Assert.That(cut.Find(".ra-card--on .ra-card-name").TextContent).IsEqualTo("Builder");
		await Assert.That(cut.Find(".ra-detail-slug").TextContent).IsEqualTo("builder");
		await Assert.That(cut.Find("input.ra-input").GetAttribute("value")).IsEqualTo("Builder");
		await Assert.That(cut.FindAll(".ra-btn--danger").Count).IsEqualTo(1);
	}

	/// <summary>
	/// A permission the game defined (<c>@role/define</c>) gets a row in the role editor under its category,
	/// with the same three states as a built-in one, and is listed by category on the Permissions tab.
	/// </summary>
	[Test]
	public async Task Roles_ShowsCustomPermissionsInTheEditorAndOnTheirTab()
	{
		_api.Bodies["api/roles"] = """
			[{"slug":"helper","name":"Helper","category":"Staff","color":"#123456","priority":12,"isSystem":false,"permissions":{"scene.close":"Allow"},"createdAt":0,"updatedAt":0}]
			""";
		_api.Bodies["api/roles/permissions"] = """
			[{"scope":"scene.close","category":"Scenes","description":"Finish any scene","createdAt":0}]
			""";
		var cut = RenderPage(typeof(AdminRoles));

		cut.WaitForAssertion(() => cut.Find(".ra-card"), TimeSpan.FromSeconds(5));
		await cut.Find(".ra-card").ClickAsync();
		var section = cut.FindAll(".ra-section").Single(s => s.QuerySelector(".ra-section-label")?.TextContent == "Scenes");
		var row = section.QuerySelectorAll(".ra-perm-row").Single(r => r.QuerySelector(".ra-perm-name")?.TextContent == "scene.close");
		// A custom permission is named by its scope, so the scope is not repeated under it.
		await Assert.That(row.QuerySelector(".ra-perm-scope")).IsNull();
		await Assert.That(row.QuerySelector(".ra-perm-desc")!.TextContent).IsEqualTo("Finish any scene");
		await Assert.That(row.QuerySelector(".ra-tri--allow")!.ClassList.Contains("ra-tri--on")).IsTrue();

		await cut.FindAll(".kit-chip").Single(chip => chip.TextContent.Contains("RolTabPermissions")).ClickAsync();
		await Assert.That(cut.Find(".roleadmin-assign .ra-section-label").TextContent).IsEqualTo("Scenes");
		await Assert.That(cut.Find(".roleadmin-assign .ra-perm-scope").TextContent).IsEqualTo("scene.close");
		await Assert.That(cut.Find(".roleadmin-assign .ra-perm-desc").TextContent).IsEqualTo("Finish any scene");
	}

	/// <summary>
	/// Each permission row shows the viewer's own access as a badge naming what decided it: the deciding roles,
	/// or the override or default. A resolution that decides every scope (owning the game) is stated once above
	/// the matrix instead.
	/// </summary>
	[Test]
	public async Task Roles_ShowsTheViewersAccessOnEachRow()
	{
		_api.Bodies["api/roles"] = """
			[{"slug":"helper","name":"Helper","category":"Staff","color":"#123456","priority":12,"isSystem":false,"permissions":{},"createdAt":0,"updatedAt":0}]
			""";
		_api.Bodies["api/roles/effective"] = """
			{"snapshots.capture":{"allowed":true,"priority":12,"roles":["helper"],"reason":"role-allow"},
			 "snapshots.restore":{"allowed":false,"priority":null,"roles":[],"reason":"account-deny"}}
			""";
		var cut = RenderPage(typeof(AdminRoles));

		cut.WaitForAssertion(() => cut.Find(".ra-card"), TimeSpan.FromSeconds(5));
		await cut.Find(".ra-card").ClickAsync();
		var capture = cut.FindAll(".ra-perm-row").Single(r => r.QuerySelector(".ra-perm-scope")?.TextContent == "snapshots.capture");
		await Assert.That(capture.QuerySelector(".ra-access--allow .ra-access-role")!.TextContent.Trim()).IsEqualTo("Helper");
		var restore = cut.FindAll(".ra-perm-row").Single(r => r.QuerySelector(".ra-perm-scope")?.TextContent == "snapshots.restore");
		await Assert.That(restore.QuerySelector(".ra-access--deny")!.TextContent).Contains("RolAccessAccountOverride");
		await Assert.That(cut.FindAll(".ra-note--access").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Roles_StatesOwnershipOnceInsteadOfOnEveryRow()
	{
		_api.Bodies["api/roles"] = """
			[{"slug":"helper","name":"Helper","category":"Staff","color":"#123456","priority":12,"isSystem":false,"permissions":{},"createdAt":0,"updatedAt":0}]
			""";
		_api.Bodies["api/roles/effective"] = """
			{"snapshots.capture":{"allowed":true,"priority":null,"roles":[],"reason":"owner"},
			 "snapshots.restore":{"allowed":true,"priority":null,"roles":[],"reason":"owner"}}
			""";
		var cut = RenderPage(typeof(AdminRoles));

		cut.WaitForAssertion(() => cut.Find(".ra-card"), TimeSpan.FromSeconds(5));
		await cut.Find(".ra-card").ClickAsync();
		await Assert.That(cut.Find(".ra-note--access").TextContent).Contains("RolAccessOwnerNote");
		await Assert.That(cut.FindAll(".ra-access").Count).IsEqualTo(0);
	}

	/// <summary>
	/// The role list is grouped by category: the group holding the highest role first, each group highest
	/// first. The editor shows the role's category.
	/// </summary>
	[Test]
	public async Task Roles_ListsRolesByCategory()
	{
		_api.Bodies["api/roles"] = """
			[{"slug":"helper","name":"Helper","category":"Staff","color":"#123456","priority":12,"isSystem":false,"permissions":{},"createdAt":0,"updatedAt":0},
			 {"slug":"wizard","name":"Wizard","category":"System","color":"#5aa9ff","priority":30,"isSystem":true,"permissions":{},"createdAt":0,"updatedAt":0},
			 {"slug":"moderator","name":"Moderator","category":"staff","color":"#ff9f6b","priority":25,"isSystem":false,"permissions":{},"createdAt":0,"updatedAt":0}]
			""";
		var cut = RenderPage(typeof(AdminRoles));

		cut.WaitForAssertion(() => cut.Find(".ra-card"), TimeSpan.FromSeconds(5));
		var list = cut.Find(".roleadmin-list");
		var order = list.Children.Select(child => child.ClassList.Contains("ra-list-group")
			? "#" + child.TextContent
			: child.QuerySelector(".ra-card-sub")!.TextContent).ToArray();
		await Assert.That(order).IsEquivalentTo(new[] { "#System", "wizard", "#staff", "moderator", "helper" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		await cut.FindAll(".ra-card").Single(card => card.TextContent.Contains("helper")).ClickAsync();
		await Assert.That(cut.Find("select.ra-input").GetAttribute("value")).IsEqualTo("Staff");
	}

	/// <summary>
	/// The Categories tab lists the role categories and the permission categories apart, each category with
	/// its description and what it holds; the role editor offers only the role categories.
	/// </summary>
	[Test]
	public async Task Roles_ListsBothCategoryListsOnTheirTab()
	{
		_api.Bodies["api/roles"] = """
			[{"slug":"helper","name":"Helper","category":"Staff","color":"#123456","priority":12,"isSystem":false,"permissions":{},"createdAt":0,"updatedAt":0}]
			""";
		_api.Bodies["api/roles/permissions"] = """
			[{"scope":"scene.close","category":"Scenes","description":"Finish any scene","createdAt":0}]
			""";
		_api.Bodies["api/roles/categories/role"] = """
			[{"name":"Staff","description":"Game staff"},{"name":"System","description":"Built in"}]
			""";
		_api.Bodies["api/roles/categories/permission"] = """
			[{"name":"Scenes","description":"Who runs scenes"}]
			""";
		var cut = RenderPage(typeof(AdminRoles));

		cut.WaitForAssertion(() => cut.Find(".ra-card"), TimeSpan.FromSeconds(5));
		await cut.Find(".ra-card").ClickAsync();
		var options = cut.Find("select.ra-input").QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray();
		await Assert.That(options).IsEquivalentTo(new[] { "", "Staff", "System" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		await cut.FindAll(".kit-chip").Single(chip => chip.TextContent.Contains("RolTabCategories")).ClickAsync();
		var roleRows = cut.FindAll("[data-kind=role] .ra-category-row");
		var permissionRows = cut.FindAll("[data-kind=permission] .ra-category-row");
		await Assert.That(roleRows.Count).IsEqualTo(2);
		await Assert.That(permissionRows.Count).IsEqualTo(1);
		await Assert.That(permissionRows[0].QuerySelectorAll("input.ra-input")[0].GetAttribute("value")).IsEqualTo("Scenes");
		await Assert.That(permissionRows[0].QuerySelectorAll("input.ra-input")[1].GetAttribute("value")).IsEqualTo("Who runs scenes");
		await Assert.That(permissionRows[0].QuerySelector(".ra-perm-desc")!.TextContent).Contains("RolCategoryHoldsPermissions");
		await Assert.That(roleRows[0].QuerySelector(".ra-perm-desc")!.TextContent).Contains("RolCategoryHoldsRoles");
	}
}
