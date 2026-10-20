using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

[NotInParallel]
public class WikiLandingDevelopmentTests
{
	[ClassDataSource<WikiLandingDevelopmentFactory>(Shared = SharedType.PerTestSession)]
	public required WikiLandingDevelopmentFactory Factory { get; init; }

	[Test]
	[Arguments("members")]
	[Arguments("ordinary-article")]
	[Arguments("navigation")]
	[Arguments("exact-article")]
	public async Task ActualDevelopmentAuthenticationRetainsItsWikiReader(string endpoint)
	{
		await Assert.That(Factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme)
			.IsEqualTo(DebugAuthenticationHandler.SchemeName);
		using (var scope = Factory.Services.CreateScope())
		{
			var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
			var authenticated = await context.AuthenticateAsync(DebugAuthenticationHandler.SchemeName);
			await Assert.That(authenticated.Succeeded).IsTrue();
			var principal = authenticated.Principal ?? throw new InvalidOperationException("Actual debug handler produced no principal.");
			await Assert.That(principal.Identity?.AuthenticationType).IsEqualTo(DebugAuthenticationHandler.SchemeName);
			var reader = await scope.ServiceProvider.GetRequiredService<WikiLandingReaderResolver>()
				.ResolveAsync(principal, CancellationToken.None);
			await Assert.That(reader.Has(PortalPermission.WikiAdmin)).IsTrue();
		}
		var package = await LandingFreshNative.InstallAsync(Factory);
		try
		{
			(await LandingFreshNative.Wiki(Factory).SetRequirementsAsync(WikiRuleTarget.ForPage(package.Page.Id),
				new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [PortalPermission.WikiAdmin] }, "#1")).Expect<None>();
			var collection = Uri.EscapeDataString("package:" + package.Manifest.Name);
			var article = "api/wiki/ns/main/" + Uri.EscapeDataString(package.Page.Slug);
			var path = endpoint switch
			{
				"members" => "api/wiki/collections/" + collection + "/pages",
				"navigation" => "api/wiki/navigation?collection=" + collection,
				"exact-article" => article + "?expectedPageId=" + Uri.EscapeDataString(package.Page.Id),
				_ => article
			};
			using var client = Factory.CreateHttpClient();
			using var response = await client.GetAsync(path);
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			if (endpoint == "members")
			{
				await Assert.That(response.Headers.GetValues("X-Total-Count").Single()).IsEqualTo("2");
				await Assert.That(json.RootElement.EnumerateArray().Any(row => row.GetProperty("id").GetString() == package.Page.Id)).IsTrue();
			}
			else if (endpoint == "navigation")
			{
				await Assert.That(json.RootElement.GetProperty("total").GetInt32()).IsEqualTo(2);
				await Assert.That(json.RootElement.GetProperty("landingPage").GetProperty("pageId").GetString()).IsEqualTo(package.Page.Id);
			}
			else await Assert.That(json.RootElement.GetProperty("id").GetString()).IsEqualTo(package.Page.Id);
		}
		finally
		{
			(await Factory.Services.GetRequiredService<IPackageInstallService>().UninstallAsync(package.Manifest.Name)).Expect<Success>();
		}
	}
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task InvalidAppBearerNeverFallsBackToDevelopmentAdmin(bool article)
	{
		var package = await LandingFreshNative.InstallAsync(Factory);
		try
		{
			using var client = Factory.CreateHttpClient();
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "smat_unissued_" + Guid.NewGuid().ToString("N"));
			var path = article
				? "api/wiki/ns/main/" + Uri.EscapeDataString(package.Page.Slug) + "?expectedPageId=" + Uri.EscapeDataString(package.Page.Id)
				: "api/wiki/navigation?collection=" + Uri.EscapeDataString("package:" + package.Manifest.Name);
			using var denied = await client.GetAsync(path);
			await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
			await Assert.That(await denied.Content.ReadAsStringAsync()).DoesNotContain(package.Page.Title);
		}
		finally
		{
			(await Factory.Services.GetRequiredService<IPackageInstallService>().UninstallAsync(package.Manifest.Name)).Expect<Success>();
		}
	}

}

public sealed class WikiLandingDevelopmentFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}
