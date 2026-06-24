using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;

namespace SharpMUSH.Tests.Integration.Http;

/// <summary>
/// Proof of concept: the default HTTP handler softcode is delivered by the
/// package manager (the <c>http-hooks</c> package, attach mode — decision 20.3),
/// not by hardcoded C#. These assertions are read-only / additive so they run
/// safely alongside the other HTTP and profile tests that share handler #8.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HttpHooksPackageTests(ServerWebAppFactory factory)
{
	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task DefaultHandler_IsInstalledAsSplitPackages_ManagingHandlerAttributes()
	{
		var http = await Registry.GetInstalledPackageAsync("http-handler");
		var profile = await Registry.GetInstalledPackageAsync("profile-handler");
		await Assert.That(http.IsT0).IsTrue();
		await Assert.That(profile.IsT0).IsTrue();

		await Assert.That((await Registry.GetPackageObjectsAsync("http-handler")).Count).IsEqualTo(0);
		await Assert.That((await Registry.GetPackageObjectsAsync("profile-handler")).Count).IsEqualTo(0);

		var httpAttrs = (await Registry.GetManagedAttributesAsync("http-handler")).Select(m => m.Attribute).ToList();
		await Assert.That(httpAttrs).Contains("GET");
		await Assert.That(httpAttrs).Contains("POST");

		var profileAttrs = (await Registry.GetManagedAttributesAsync("profile-handler")).Select(m => m.Attribute).ToList();
		await Assert.That(profileAttrs).Contains("GET`CHARACTERS");
		await Assert.That(profileAttrs).Contains("GET`PROFILE`SCHEMA");
		await Assert.That(profileAttrs).DoesNotContain("GET");

		var profileDeps = await Registry.GetPackageDependenciesAsync("profile-handler");
		await Assert.That(profileDeps.Select(d => d.DependsOnId)).Contains("http-handler");
	}

	[Test]
	public async Task HttpHooks_PackageManagedRoutes_RespondLive()
	{
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/profile/schema");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
		var body = await response.Content.ReadAsStringAsync();
		await Assert.That(body).Contains("sections");
	}
}
