using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.BUnit.Controllers;

public class ApplicationConfigurationTests
{
	[Test]
	public async Task TilesRequireTheirOwnPermissionAndSafeLocalRoutes()
	{
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns(Task.FromResult<IReadOnlyList<RegisteredApplication>>([]));
		var authorization = Substitute.For<IAuthorizationService>();
		authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
			.Returns(call => Task.FromResult(call.Arg<string>() == "discord.admin"
				? AuthorizationResult.Success() : AuthorizationResult.Failed()));
		var catalog = PluginCatalog.ForPlugins([new TilePlugin()]);
		var controller = new ApplicationConfigurationController(registry, catalog, authorization,
			NullLogger<ApplicationConfigurationController>.Instance)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

		var result = await controller.List();
		var tiles = (IReadOnlyList<ApplicationConfigurationTile>)((OkObjectResult)result.Result!).Value!;
		await Assert.That(tiles.Select(tile => tile.Id)).IsEquivalentTo(new[] { "discord" });
		await Assert.That(tiles.Single().Route).IsEqualTo("/admin/discord");
	}

	[Test]
	[Arguments("/admin/discord", true)]
	[Arguments("/apps/example/settings", true)]
	[Arguments("//evil.example/admin", false)]
	[Arguments("https://evil.example/admin", false)]
	[Arguments("/admin/../login", false)]
	[Arguments("/admin/%2f%2fevil", false)]
	[Arguments("/admin/discord?next=https://evil.example", false)]
	[Arguments("/admin/discord#fragment", false)]
	[Arguments("/admin/discord\\evil", false)]
	public async Task TileRoutesAreCanonicalLocalPaths(string route, bool expected)
	{
		await Assert.That(new ApplicationConfigurationTile("test", "Test", "Settings", null, route, "test.admin")
			.HasSafeRoute()).IsEqualTo(expected);
	}

	[Test]
	public async Task RegistryContributionsDisappearAfterApplicationRemovalAndPluginTilesRequireLoadedSource()
	{
		var registry = new SharpMUSH.Tests.Shared.FakeApplicationRegistry();
		var tile = new ApplicationConfigurationTile("softcode", "Softcode", "Settings", null, "/apps/example", "example.admin");
		await registry.UpsertApplicationAsync(new RegisteredApplication("example", "Example", null,
			ApplicationKind.Page, "http/example/schema", null, null, PortalRole.Guest, null, null, 0,
			ConfigurationTile: tile));
		var authorization = Substitute.For<IAuthorizationService>();
		authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
			.Returns(Task.FromResult(AuthorizationResult.Success()));
		var controller = new ApplicationConfigurationController(registry, PluginCatalog.Empty(), authorization,
			NullLogger<ApplicationConfigurationController>.Instance)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		var present = (IReadOnlyList<ApplicationConfigurationTile>)((OkObjectResult)(await controller.List()).Result!).Value!;
		await Assert.That(present.Select(item => item.Id)).IsEquivalentTo(new[] { "softcode" });
		await registry.RemoveApplicationAsync("example");
		var absent = (IReadOnlyList<ApplicationConfigurationTile>)((OkObjectResult)(await controller.List()).Result!).Value!;
		await Assert.That(absent.Count).IsEqualTo(0);
	}

	[Test]
	[Arguments("/apps/example/settings", true)]
	[Arguments("//external.example", false)]
	public async Task PackageManifestValidatesConfigurationTiles(string route, bool valid)
	{
		var yaml = $"""
			package: example-app
			version: 1.0.0
			kind: application
			application:
			  slug: example
			  display_name: Example
			  schema_url: http/example/schema
			  configuration_tile:
			    id: example
			    title: Example configuration
			    description: Settings
			    route: {route}
			    permission: config.admin
			""";
		var parsed = new SharpMUSH.Library.Services.PackageManifestService().ParseManifest(yaml);
		await Assert.That(parsed is SharpMUSH.Library.Models.Packages.ParsedPackageManifest).IsEqualTo(valid);
		if (parsed is SharpMUSH.Library.Models.Packages.ParsedPackageManifest package)
		{
			await Assert.That(package.Manifest.Application!.ConfigurationTile!.Route).IsEqualTo(route);
			var written = SharpMUSH.Library.Services.PackageManifestWriter.Write(package.Manifest);
			var roundTrip = new SharpMUSH.Library.Services.PackageManifestService().ParseManifest(written)
				.Expect<SharpMUSH.Library.Models.Packages.ParsedPackageManifest>();
			await Assert.That(roundTrip.Manifest.Application!.ConfigurationTile).IsEqualTo(package.Manifest.Application.ConfigurationTile);
		}
	}

	[Test]
	public async Task DiscoveryEndpointRequiresConfigurationAdministration()
	{
		var authorize = typeof(ApplicationConfigurationController)
			.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
		await Assert.That(authorize.Policy).IsEqualTo(PortalPermission.ConfigAdmin);
	}

	private sealed class TilePlugin : PluginBase, IApplicationSource
	{
		public override string Id => "tiles";
		public IEnumerable<RegisteredApplication> GetApplications() => [];
		public IEnumerable<ApplicationConfigurationTile> GetConfigurationTiles() =>
		[
			new("discord", "Discord", "Bot settings", "Forum", "/admin/discord", "discord.admin"),
			new("private", "Private", "Hidden settings", "Lock", "/admin/private", "private.admin"),
			new("external", "External", "Bad link", "Link", "//example.com/admin", "discord.admin"),
			new("traversal", "Traversal", "Bad link", "Link", "/admin/../login", "discord.admin")
		];
	}
}
