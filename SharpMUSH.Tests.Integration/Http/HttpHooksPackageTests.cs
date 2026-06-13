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
/// safely alongside the other HTTP and profile tests that share handler #4.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HttpHooksPackageTests(ServerWebAppFactory factory)
{
	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task HttpHooks_IsInstalledAsAPackage_ManagingHandlerAttributes()
	{
		// The bootstrap installed the bundled package at startup.
		var installed = await Registry.GetInstalledPackageAsync("http-hooks");
		await Assert.That(installed.IsT0).IsTrue();
		await Assert.That(installed.AsT0.Version).IsEqualTo("1.0.0");

		// Attach mode: it manages attributes but owns no objects (#4 is infrastructure).
		await Assert.That((await Registry.GetPackageObjectsAsync("http-hooks")).Count).IsEqualTo(0);

		var managed = await Registry.GetManagedAttributesAsync("http-hooks");
		var attrs = managed.Select(m => m.Attribute).ToList();
		await Assert.That(attrs).Contains("GET");
		await Assert.That(attrs).Contains("GET`CHARACTERS");
		await Assert.That(attrs).Contains("GET`PROFILE`SCHEMA");
		// Every managed attribute lives on the one handler object.
		await Assert.That(managed.Select(m => m.Objid).Distinct().Count()).IsEqualTo(1);
	}

	[Test]
	public async Task HttpHooks_PackageManagedRoutes_RespondLive()
	{
		// The package-installed softcode actually serves requests — identical
		// behavior to the old hardcoded seeding.
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/profile/schema");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
		var body = await response.Content.ReadAsStringAsync();
		await Assert.That(body).Contains("sections");
	}
}
