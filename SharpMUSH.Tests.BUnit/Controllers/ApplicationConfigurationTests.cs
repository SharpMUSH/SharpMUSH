using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Server.Authentication;
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
	[Arguments(true)]
	[Arguments(false)]
	public async Task TilesRequireTheirOwnPermissionAndSafeLocalRoutes(bool granted)
	{
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns(Task.FromResult<IReadOnlyList<RegisteredApplication>>([]));
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(new CapabilityActor("admin"), "discord.admin", Arg.Any<CancellationToken>()).Returns(granted);
		var services = new ServiceCollection();
		services.AddLogging().AddAuthorizationCore();
		services.AddOptions<AuthenticationOptions>();
		services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
		services.AddSingleton<IAuthorizationHandler>(new PermissionAuthorizationHandler(capabilities,
			new DelegatedPermissions(capabilities, Substitute.For<IRoleRegistryService>(), new HttpContextAccessor())));
		using var provider = services.BuildServiceProvider();
		var authorization = provider.GetRequiredService<IAuthorizationService>();
		var catalog = PluginCatalog.ForPlugins([new TilePlugin()]);
		var controller = new ApplicationConfigurationController(registry, catalog, authorization,
			NullLogger<ApplicationConfigurationController>.Instance)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity([
				new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(PortalPermission.ClaimType, "discord.admin")], "test"))
				}
			}
		};

		var result = await controller.List();
		var tiles = (IReadOnlyList<ApplicationConfigurationTile>)((OkObjectResult)result.Result!).Value!;
		await Assert.That(tiles.Select(tile => tile.Id)).IsEquivalentTo(granted ? new[] { "discord" } : []);
		if (granted) await Assert.That(tiles.Single().Route).IsEqualTo("/admin/discord");
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
		authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<IEnumerable<IAuthorizationRequirement>>())
			.Returns(Task.FromResult(AuthorizationResult.Success()));
		var controller = new ApplicationConfigurationController(registry, PluginCatalog.Empty(), authorization,
			NullLogger<ApplicationConfigurationController>.Instance)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		var present = (IReadOnlyList<ApplicationConfigurationTile>)((OkObjectResult)(await controller.List()).Result!).Value!;
		await Assert.That(present.Where(item => !item.Id.StartsWith("signin-", StringComparison.Ordinal)).Select(item => item.Id)).IsEquivalentTo(new[] { "softcode" });
		await registry.RemoveApplicationAsync("example");
		var absent = (IReadOnlyList<ApplicationConfigurationTile>)((OkObjectResult)(await controller.List()).Result!).Value!;
		await Assert.That(absent.Select(item => item.Id)).IsEquivalentTo(new[] { "signin-discord", "signin-google", "signin-microsoft", "signin-grapevine" });
	}

	[Test]
	[Arguments("/apps/example/settings", true)]
	[Arguments("//external.example", false)]
	public async Task PackageManifestValidatesConfigurationTiles(string route, bool valid)
	{
		var yaml = $"""
			format: 1.11
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
	[Arguments("line one\nline two")]
	[Arguments("line one\r\nline two")]
	[Arguments("line one\u2028line two\u2029end")]
	public async Task ConfigurationTileTextPreservesLineBreaksWhenExported(string text)
	{
		var parsed = new SharpMUSH.Library.Services.PackageManifestService().ParseManifest("format: 1.11\npackage: example-app\nversion: 1.0.0\nkind: application\napplication:\n  slug: example\n  display_name: Example\n  schema_url: http/example/schema\n")
			.Expect<SharpMUSH.Library.Models.Packages.ParsedPackageManifest>();
		var tile = new ApplicationConfigurationTile("example", text, text, text, "/apps/example/settings", "config.admin");
		var manifest = parsed.Manifest with { Application = parsed.Manifest.Application! with { ConfigurationTile = tile } };
		var written = SharpMUSH.Library.Services.PackageManifestWriter.Write(manifest);
		var roundTrip = new SharpMUSH.Library.Services.PackageManifestService().ParseManifest(written)
			.Expect<SharpMUSH.Library.Models.Packages.ParsedPackageManifest>();
		await Assert.That(roundTrip.Manifest.Application!.ConfigurationTile).IsEqualTo(tile);
	}

	[Test]
	[Arguments("1.10", false)]
	[Arguments("1.11", true)]
	[Arguments(null, true)] // The current format must continue supporting configuration tiles.
	public async Task ConfigurationTileRequiresItsIntroducedManifestFormat(string? format, bool supported)
	{
		format ??= SharpMUSH.Library.Models.Packages.PackageFormatVersion.Supported.ToString();
		var yaml = $"""
			format: {format}
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
			    route: /apps/example/settings
			    permission: config.admin
			""";
		var package = new SharpMUSH.Library.Services.PackageManifestService().ParseManifest(yaml)
			.Expect<SharpMUSH.Library.Models.Packages.ParsedPackageManifest>();
		await Assert.That(package.Manifest.Application!.ConfigurationTile is not null).IsEqualTo(supported);
		await Assert.That(package.Warnings.Any(issue => issue.Path == "application.configuration_tile")).IsEqualTo(!supported);
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
			null!, // A third-party plugin can violate its non-nullable contribution contract.
			new("discord", "Discord", "Bot settings", "Forum", "/admin/discord", "discord.admin"),
			new("private", "Private", "Hidden settings", "Lock", "/admin/private", "private.admin"),
			new("external", "External", "Bad link", "Link", "//example.com/admin", "discord.admin"),
			new("traversal", "Traversal", "Bad link", "Link", "/admin/../login", "discord.admin")
		];
	}
}
