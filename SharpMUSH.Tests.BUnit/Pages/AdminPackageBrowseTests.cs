using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
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
		await Assert.That(cut.Find(".pkg-installed-marker").TextContent).Contains($"PkgBrowseInstalled({version})");
		await Assert.That(cut.Markup.Contains("PkgBrowseInstalledOrigin")).IsEqualTo(differentOrigin);
		if (differentOrigin)
			await Assert.That(cut.Markup).Contains($"PkgBrowseInstalledOrigin({repo}, {path})");
		await cut.FindAll("button").Single(b => b.TextContent == "PkgReviewInstall").ClickAsync();
		await Assert.That(Services.GetRequiredService<NavigationManager>().Uri)
			.Contains("/admin/packages/review?remote=official&path=tools&version=1.0.0");
	}

	[Test]
	[Arguments("tools")]
	[Arguments(null)]
	public async Task Uninstalled_and_unparseable_entries_have_no_installed_marker(string? packageId)
	{
		api.Installed = [Installed("1.0.0", Repo, "tools") with { Package = Installed("1.0.0", Repo, "tools").Package with { Id = packageId is null ? "tools" : "another-package" } }];
		api.PackageId = packageId;
		var cut = RenderBrowse();
		await Assert.That(cut.FindAll(".pkg-installed-marker")).IsEmpty();
		if (packageId is null)
			await Assert.That(cut.Markup).Contains("PkgManifestParseFailed");
	}

	[Test]
	public async Task Refresh_reloads_installed_state()
	{
		var cut = RenderBrowse();
		await Assert.That(cut.FindAll(".pkg-installed-marker")).IsEmpty();
		api.Installed = [Installed("0.9.0", Repo, "tools")];
		await cut.FindAll("button").Single(b => b.TextContent == "PkgRefreshAndBrowse").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".pkg-installed-marker"));
		await Assert.That(cut.Find(".pkg-installed-marker").TextContent).Contains("PkgBrowseInstalled(0.9.0)");
	}

	[Test]
	public async Task Installed_api_failure_clears_stale_markers_and_shows_status_warning()
	{
		api.Installed = [Installed("1.0.0", Repo, "tools")];
		var cut = RenderBrowse();
		api.FailInstalled = true;
		await cut.FindAll("button").Single(b => b.TextContent == "PkgRefreshAndBrowse").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".pkg-installed-warning"));
		await Assert.That(cut.FindAll(".pkg-installed-marker")).IsEmpty();
		await Assert.That(cut.Find(".pkg-installed-warning").TextContent).Contains("PkgBrowseInstalledUnavailable");
		await Assert.That(cut.Markup).Contains("Test installed status failure");
		await Assert.That(cut.Markup).Contains("PkgReviewInstall");
	}

	[After(Test)]
	public void DisposeClient() => client.Dispose();

	private IRenderedComponent<AdminPackageBrowse> RenderBrowse()
	{
		var host = Render<MudHarness>(p => p.AddChildContent<AdminPackageBrowse>());
		var cut = host.FindComponent<AdminPackageBrowse>();
		cut.WaitForAssertion(() => cut.Find(".pkg-card-body"));
		return cut;
	}

	private static InstalledPackageDto Installed(string version, string repo, string path) =>
		new(new InstalledPackageRecord("tools", version, repo, path, "abc123", null, DateTimeOffset.UtcNow, 1), 0, 0, []);

	private sealed class BrowseApiHandler : HttpMessageHandler
	{
		public IReadOnlyList<InstalledPackageDto> Installed { get; set; } = [];
		public bool FailInstalled { get; set; }
		public string? PackageId { get; set; } = "tools";

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			return Task.FromResult(path switch
			{
				"/api/packages/remotes" => Json(new[] { new PackageRemoteRecord("official", Repo, PackageRemoteTrust.Official, null) }),
				"/api/packages/remotes/official/browse" => Json(new PackageRepoSnapshot("official", Repo, "abc123",
					[new("tools", PackageId, "1.1.0", "Useful tools", [new("1.0.0", "tools/v1.0.0", "abc123")])])),
				"/api/packages" when FailInstalled => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
				{ Content = new StringContent("Test installed status failure") },
				"/api/packages" => Json(Installed),
				_ => new HttpResponseMessage(HttpStatusCode.NotFound)
			});
		}

		private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
	}
}
