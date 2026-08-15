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

/// <summary>
/// HttpMessageHandler faking the package admin API surface: GET api/packages returns a single
/// fixed installed package so tests can assert on rendered content deterministically.
/// </summary>
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
			// Assigned to a local rather than constructed inline in the `return`, so the object
			// isn't flagged as an undisposed disposable at its construction site — disposal is
			// the caller's job here (GetFromJsonAsync disposes the response once it has read the
			// content), but a locally-scoped reference makes that ownership transfer explicit.
			var okResponse = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create<IReadOnlyList<InstalledPackageDto>>(installed)
			};
			return Task.FromResult(okResponse);
		}

		var notFoundResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
		return Task.FromResult(notFoundResponse);
	}
}

/// <summary>Helper to register a real PackagesAdminService backed by <see cref="AdminPackagesApiHandler"/>.</summary>
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
			.AddSingleton(sp => new PackagesAdminService(sp.GetRequiredService<IHttpClientFactory>()))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		return apiClient;
	}
}

/// <summary>
/// Regression coverage for the Razor email-address heuristic bug: <c>v@p.Package.Version</c>
/// in AdminPackages.razor was parsed by Razor as literal text (the "looks like an email"
/// heuristic — a non-whitespace char before <c>@</c> followed by a dotted identifier run)
/// instead of a code transition, so the page rendered the literal string
/// <c>v@p.Package.Version</c> rather than the installed version. Fixed with the explicit
/// expression form <c>v@(p.Package.Version)</c>.
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
		// MudTooltip (revision history / uninstall buttons) needs a MudPopoverProvider in the render tree.
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

	public new async ValueTask DisposeAsync()
	{
		ownedHttpClient.Dispose();
		await base.DisposeAsync();
	}
}
