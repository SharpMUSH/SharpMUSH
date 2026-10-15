using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

file sealed class AdminPackagesApiHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Get && path == "api/packages")
		{
			var installed = new List<InstalledPackageDto>
			{
				new(
					new InstalledPackageRecord(
						"who-where",
						"1.2.0",
						"https://example.com/who-where.git",
						null,
						"abc123",
						null,
						DateTimeOffset.UtcNow,
						1),
					ManagedAttributeCount: 3,
					ObjectCount: 2,
					Dependents: [])
			};
			var okResponse = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create<IReadOnlyList<InstalledPackageDto>>(installed)
			};
			return Task.FromResult(okResponse);
		}

		if (request.Method == HttpMethod.Get && path == "api/packages/who-where/readme")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new ReadmeResponse("# who-where", "<h1>who-where readme</h1>"))
			});
		}

		var notFoundResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
		return Task.FromResult(notFoundResponse);
	}
}

file static class AdminPackagesTestServices
{
	public static HttpClient AddAdminPackagesTestServices(this BunitContext ctx)
	{
		var apiClient = new HttpClient(new AdminPackagesApiHandler())
		{
			BaseAddress = new Uri("https://localhost:8081/")
		};

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		ctx.Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new PackagesAdminService(sp.GetRequiredService<IHttpClientFactory>(),
				new StubServerInfoService(guestsEnabled: true)))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		return apiClient;
	}
}

/// <summary>
/// Razor reads <c>v@p.Package.Version</c> as an email address, not a code transition, so
/// AdminPackages.razor has to spell it <c>v@(p.Package.Version)</c>.
/// </summary>
public class AdminPackagesPageTests : BunitContext, IAsyncDisposable
{
	private readonly HttpClient ownedHttpClient;
	private BunitAuthorizationContext Auth { get; }

	public AdminPackagesPageTests()
	{
		Auth = this.AddAuthorization();
		Auth.SetAuthorized("headwiz");
		Auth.SetPolicies(PortalPermission.PackagesAdmin);
		ownedHttpClient = this.AddAdminPackagesTestServices();
	}

	[TUnit.Core.Test]
	public async Task RendersInstalledVersion_NotTheLiteralExpressionText()
	{
		var host = Render<MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.Admin.Packages.AdminPackages>());
		var cut = host.FindComponent<SharpMUSH.Client.Pages.Admin.Packages.AdminPackages>();

		host.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("who-where"))
				throw new InvalidOperationException("package rows not rendered yet");
		});

		await Assert.That(cut.Markup).Contains("v1.2.0");
		await Assert.That(cut.Markup).DoesNotContain("@p.Package.Version");
	}

	/// <summary>An installed package's README button opens its README, as the server rendered it.</summary>
	[TUnit.Core.Test]
	public async Task ReadmeButton_ShowsTheInstalledPackagesReadme()
	{
		var cut = Render(builder =>
		{
			builder.OpenComponent<MudBlazor.MudDialogProvider>(0);
			builder.CloseComponent();
			builder.OpenComponent<MudHarness>(1);
			builder.AddAttribute(2, nameof(MudHarness.ChildContent), (Microsoft.AspNetCore.Components.RenderFragment)(b =>
			{
				b.OpenComponent<SharpMUSH.Client.Pages.Admin.Packages.AdminPackages>(0);
				b.CloseComponent();
			}));
			builder.CloseComponent();
		});
		const string button = "button[aria-label='PkgShowReadme(who-where)']";
		cut.WaitForAssertion(() => cut.Find(button), TimeSpan.FromSeconds(5));

		await cut.Find(button).ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("<h1>who-where readme</h1>"))
				throw new InvalidOperationException("README not shown yet");
		}, TimeSpan.FromSeconds(5));
	}

	public new async ValueTask DisposeAsync()
	{
		ownedHttpClient.Dispose();
		await base.DisposeAsync();
	}
}
