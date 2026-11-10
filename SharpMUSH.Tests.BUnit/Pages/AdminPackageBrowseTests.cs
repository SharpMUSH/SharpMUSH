using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Pages.Admin.Packages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class AdminPackageBrowseTests : BunitContext
{
	private const string Repo = "https://example.com/packages.git";
	private readonly BrowseApiHandler api = new();
	private readonly HttpClient client;

	public AdminPackageBrowseTests()
	{
		client = new HttpClient(api) { BaseAddress = new Uri("https://localhost/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices().AddSingleton(factory)
			.AddSingleton(new PackagesAdminService(factory, new StubServerInfoService(true)))
			.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/packages/browse?remote=official");
	}

	[Test]
	[Arguments("1.0.0", Repo, "tools", false)]
	[Arguments("0.9.0", Repo, "tools", false)]
	[Arguments("1.0.0", "https://example.com/other.git", "tools", true)]
	[Arguments("1.0.0", Repo, "other-path", true)]
	public async Task Installed_id_shows_actual_version_and_origin_without_disabling_review(
		string version, string repo, string path, bool differentOrigin)
	{
		api.Installed = [Installed(version, repo, path)];
		var cut = RenderBrowse();
		await Assert.That(cut.Find(".lib-installed").TextContent).Contains($"PkgBrowseInstalled({version})");
		await Assert.That(cut.Markup.Contains("PkgBrowseInstalledOrigin")).IsEqualTo(differentOrigin);
		if (differentOrigin)
			await Assert.That(cut.Markup).Contains($"PkgBrowseInstalledOrigin({repo}, {path})");
		await cut.FindAll("button").Single(b => b.TextContent == "PkgReviewInstall").ClickAsync();
		await Assert.That(Services.GetRequiredService<NavigationManager>().Uri)
			.Contains("/admin/packages/review?remote=official&path=tools&version=1.0.0");
	}

	[Test]
	[Arguments(null, "PkgBatchQueueInstall")]
	[Arguments("1.0.0", "PkgBatchQueueReinstall")]
	[Arguments("0.9.0", "PkgBatchQueueChange(1.0.0)")]
	public async Task The_queue_button_says_what_queueing_does_to_an_installed_package(string? installed, string label)
	{
		api.Installed = installed is null ? [] : [Installed(installed, Repo, "tools")];
		var cut = RenderBrowse();
		await Assert.That(cut.Find(".lib-row .pkg-btn-primary").TextContent.Trim()).IsEqualTo(label);
	}

	[Test]
	public async Task Needed_packages_are_marked_installed_or_missing()
	{
		api.Packages = [new("tools", "tools", "1.0.0", "Tools", [], DependsOn: ["dice", "plus-help"])];
		api.Installed = [Installed("1.0.0", Repo, "dice") with { Package = Installed("1.0.0", Repo, "dice").Package with { Id = "dice" } }];
		var cut = RenderBrowse();
		var needs = cut.FindAll(".lib-need").ToDictionary(n => n.TextContent.Trim());
		await Assert.That(needs.Keys).IsEquivalentTo(["dice", "plus-help"]);
		await Assert.That(needs["dice"].ClassList).DoesNotContain("lib-need--missing");
		await Assert.That(needs["plus-help"].ClassList).Contains("lib-need--missing");
		await Assert.That(needs["plus-help"].GetAttribute("title")).IsEqualTo("PkgBrowseNeedMissing(plus-help)");
	}

	[Test]
	public async Task Needed_packages_are_not_called_missing_when_the_installed_list_failed()
	{
		api.Packages = [new("tools", "tools", "1.0.0", "Tools", [], DependsOn: ["dice"])];
		api.FailInstalled = true;
		var cut = RenderBrowse();
		await Assert.That(cut.Find(".lib-need").ClassList).DoesNotContain("lib-need--missing");
	}

	[Test]
	[Arguments("tools")]
	[Arguments(null)]
	public async Task Uninstalled_and_unparseable_entries_have_no_installed_marker(string? packageId)
	{
		api.Installed = [Installed("1.0.0", Repo, "tools") with { Package = Installed("1.0.0", Repo, "tools").Package with { Id = packageId is null ? "tools" : "another-package" } }];
		api.PackageId = packageId;
		var cut = RenderBrowse();
		await Assert.That(cut.FindAll(".lib-installed")).IsEmpty();
		if (packageId is null)
			await Assert.That(cut.Markup).Contains("PkgManifestParseFailed");
	}

	[Test]
	public async Task Refresh_reloads_installed_state()
	{
		var cut = RenderBrowse();
		await Assert.That(cut.FindAll(".lib-installed")).IsEmpty();
		api.Installed = [Installed("0.9.0", Repo, "tools")];
		await cut.FindAll("button").Single(b => b.TextContent == "PkgRefreshAndBrowse").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".lib-installed"));
		await Assert.That(cut.Find(".lib-installed").TextContent).Contains("PkgBrowseInstalled(0.9.0)");
	}

	[Test]
	public async Task Installed_api_failure_clears_stale_markers_and_shows_status_warning()
	{
		api.Installed = [Installed("1.0.0", Repo, "tools")];
		var cut = RenderBrowse();
		api.FailInstalled = true;
		await cut.FindAll("button").Single(b => b.TextContent == "PkgRefreshAndBrowse").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".pkg-installed-warning"));
		await Assert.That(cut.FindAll(".lib-installed")).IsEmpty();
		await Assert.That(cut.Find(".pkg-installed-warning").TextContent).Contains("PkgBrowseInstalledUnavailable");
		await Assert.That(cut.Markup).Contains("Test installed status failure");
		await Assert.That(cut.Markup).Contains("PkgReviewInstall");
	}

	[Test]
	public async Task Queueing_a_bridge_queues_what_it_joins_that_is_neither_installed_nor_queued()
	{
		api.Packages =
		[
			new("sheet", "sheet", "1.0.0", "A sheet", [new("1.0.0", "sheet/v1.0.0", "abc123")], Category: "Game System", Subcategory: "Fate",
				DependsOn: ["dice"]),
			new("chargen", "chargen", "1.0.0", "Chargen", [new("1.0.0", "chargen/v1.0.0", "abc123")]),
			new("sheet-chargen", "sheet-chargen", "1.0.0", "Bridge", [new("1.0.0", "sheet-chargen/v1.0.0", "abc123")], Category: "Game System",
				Subcategory: "Fate", Bridge: true, DependsOn: ["sheet", "chargen"])
		];
		api.Catalogue = [new("dice", "dice", "1.0.0", "Dice", [])];
		api.Installed = [Installed("1.0.0", Repo, "chargen") with { Package = Installed("1.0.0", Repo, "chargen").Package with { Id = "chargen" } }];
		var cut = RenderBrowse();
		await cut.FindAll(".lib-addons-toggle").Single().ClickAsync();
		var bridge = cut.FindComponents<LibraryPackageRow>().Single(r => r.Instance.Entry.PackageId == "sheet-chargen");
		await bridge.InvokeAsync(() => bridge.Instance.OnQueue.InvokeAsync());
		var packages = Services.GetRequiredService<PackagesAdminService>();
		await Assert.That(packages.Batch.Intents.Select(i => i.TargetId)).IsEquivalentTo(["dice", "sheet", "sheet-chargen"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(packages.Batch.Intents.Single(i => i.TargetId == "sheet").Source?.Version).IsEqualTo("1.0.0");
		await Assert.That(packages.Batch.Intents.Single(i => i.TargetId == "dice").Source).IsEqualTo(new PackageBatchSource("bundled", "dice"))
			.Because("a dependency this remote does not list comes from the bundled catalogue");
		await Assert.That(packages.Batch.PrerequisiteReason(packages.Batch.Intents.Single(i => i.TargetId == "dice").ClientIntentId!))
			.IsEqualTo("PkgBatchNeededBy(sheet-chargen)");
		await Assert.That(packages.Batch.PrerequisiteReason(packages.Batch.Intents.Single(i => i.TargetId == "sheet-chargen").ClientIntentId!)).IsNull();
	}

	[Test]
	public async Task A_bridge_keeps_its_kinds_icon_with_a_bridge_badge()
	{
		api.Packages =
		[
			new("sheet", "sheet", "1.0.0", "A sheet", []),
			new("sheet-help", "sheet-help", "1.0.0", "Bridge", [], Bridge: true, DependsOn: ["sheet", "plus-help"])
		];
		var cut = RenderBrowse();
		await cut.FindAll(".lib-addons-toggle").Single().ClickAsync();

		var rows = cut.FindComponents<LibraryPackageRow>().ToDictionary(r => r.Instance.Entry.PackageId!);
		await Assert.That(rows["sheet-help"].Find(".pkg-row-icon").ClassList).Contains("pkg-kind-softcode");
		await Assert.That(rows["sheet-help"].FindAll(".pkg-row-bridge")).HasCount().EqualTo(1);
		await Assert.That(rows["sheet-help"].Find(".pkg-row-icon-wrap").GetAttribute("title")).StartsWith("PkgKindBridgeOf(");
		await Assert.That(rows["sheet"].FindAll(".pkg-row-bridge")).IsEmpty();
	}

	[Test]
	public async Task The_dependency_graph_draws_the_listed_packages_with_what_they_need()
	{
		api.Packages =
		[
			new("sheet", "sheet", "1.0.0", "A sheet", [], Title: "Fate sheet", DependsOn: ["dice"]),
			new("sheet-help", "sheet-help", "1.0.0", "Bridge", [], Bridge: true, DependsOn: ["sheet", "plus-help"]),
			new("tools", "tools", "1.0.0", "Tools", [])
		];
		api.Installed = [Installed("1.0.0", Repo, "dice") with { Package = Installed("1.0.0", Repo, "dice").Package with { Id = "dice" } }];
		var cut = RenderBrowse();

		await cut.FindAll(".lib-views .lib-chip")[1].ClickAsync();

		var graph = cut.FindComponent<PackageGraph>().Instance;
		await Assert.That(graph.Nodes.Select(n => (n.Id, n.State, n.Bridge))).IsEquivalentTo(new[]
		{
			("sheet", PackageGraphState.Available, false),
			("sheet-help", PackageGraphState.Available, true),
			("tools", PackageGraphState.Available, false),
			("dice", PackageGraphState.Installed, false),
			("plus-help", PackageGraphState.Missing, false),
		});
		await Assert.That(cut.FindAll(".lib-row")).IsEmpty().Because("the graph replaces the list");
		await Assert.That(cut.Find(".pkg-graph-node[aria-label^='PkgGraphNodeLabel(Fate sheet,']").ClassList).Contains("pkg-graph-node--available");

		await cut.Find("input[name=library-search]").InputAsync("help");
		await Assert.That(cut.FindComponent<PackageGraph>().Instance.Nodes.Select(n => n.Id).Order())
			.IsEquivalentTo(new[] { "dice", "plus-help", "sheet", "sheet-help" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("a filtered graph keeps what its packages need");
	}

	[Test]
	public async Task Remote_failure_stays_visible_until_successful_retry()
	{
		api.FailRemotes = true;
		var host = Render<MudHarness>(p => p.AddChildContent<AdminPackageBrowse>());
		var cut = host.FindComponent<AdminPackageBrowse>();
		cut.WaitForAssertion(() => cut.Find(".pkg-remotes-error"));
		await Assert.That(cut.FindAll(".lib-row")).IsEmpty();
		api.FailRemotes = false;
		await cut.Find(".pkg-remotes-retry").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".lib-row"));
		await Assert.That(cut.FindAll(".pkg-remotes-error")).IsEmpty();
	}

	[Test]
	public async Task Remote_management_failures_stop_loading_and_retry_each_source()
	{
		api.FailRemotes = true;
		api.FailCommunity = true;
		var host = Render<MudHarness>(p => p.AddChildContent<AdminPackageRemotes>());
		var cut = host.FindComponent<AdminPackageRemotes>();
		cut.WaitForAssertion(() => cut.Find(".pkg-community-error"));
		await Assert.That(cut.FindAll(".pkg-remotes-error")).HasCount().EqualTo(1);
		await Assert.That(cut.Markup).DoesNotContain("PkgNoCommunityRepos");
		api.FailRemotes = false;
		api.FailCommunity = false;
		await cut.Find(".pkg-remotes-retry").ClickAsync();
		await cut.Find(".pkg-community-retry").ClickAsync();
		await Assert.That(cut.FindAll(".pkg-remotes-error, .pkg-community-error")).IsEmpty();
		await Assert.That(cut.Find(".pkg-remotes-table").TextContent).Contains("official");
		await Assert.That(cut.Markup).Contains("PkgNoCommunityRepos");
	}

	[After(Test)]
	public void DisposeClient() => client.Dispose();

	private IRenderedComponent<AdminPackageBrowse> RenderBrowse()
	{
		var host = Render<MudHarness>(p => p.AddChildContent<AdminPackageBrowse>());
		var cut = host.FindComponent<AdminPackageBrowse>();
		cut.WaitForAssertion(() => cut.Find(".lib-row"));
		return cut;
	}

	private static InstalledPackageDto Installed(string version, string repo, string path) =>
		new(new InstalledPackageRecord("tools", version, repo, path, "abc123", null, DateTimeOffset.UtcNow, 1), 0, 0, []);

	private sealed class BrowseApiHandler : HttpMessageHandler
	{
		public IReadOnlyList<InstalledPackageDto> Installed { get; set; } = [];
		public bool FailInstalled { get; set; }
		public bool FailRemotes { get; set; }
		public bool FailCommunity { get; set; }
		public string? PackageId { get; set; } = "tools";
		public IReadOnlyList<PackageRepoEntry>? Packages { get; set; }
		public IReadOnlyList<PackageRepoEntry> Catalogue { get; set; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			return Task.FromResult(path switch
			{
				"/api/packages/community" when FailCommunity => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
				"/api/packages/community" => Json(new CommunityReposResponse([], [])),
				"/api/packages/remotes" when FailRemotes => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
				"/api/packages/remotes" => Json(new[] { new PackageRemoteRecord("official", Repo, PackageRemoteTrust.Official, null) }),
				"/api/packages/remotes/official/browse" => Json(new PackageRepoSnapshot("official", Repo, "abc123",
					Packages ?? [new("tools", PackageId, "1.1.0", "Useful tools", [new("1.0.0", "tools/v1.0.0", "abc123")])])),
				"/api/packages/remotes/bundled/browse" => Json(new PackageRepoSnapshot("bundled", "bundled", "abc123", Catalogue)),
				"/api/packages" when FailInstalled => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
				{ Content = new StringContent("Test installed status failure") },
				"/api/packages" => Json(Installed),
				_ => new HttpResponseMessage(HttpStatusCode.NotFound)
			});
		}

		private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
	}
}
