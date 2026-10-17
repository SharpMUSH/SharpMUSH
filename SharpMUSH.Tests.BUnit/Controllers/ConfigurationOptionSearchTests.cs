using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Tests.BUnit.Controllers;

public class ConfigurationOptionSearchTests
{
	private static ApplicationConfigurationController Controller(IApplicationRegistryService registry, params string[] scopes)
	{
		var auth = Substitute.For<IAuthorizationService>();
		auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<IEnumerable<IAuthorizationRequirement>>())
			.Returns(call => Task.FromResult(call.Arg<IEnumerable<IAuthorizationRequirement>>().OfType<PermissionRequirement>().All(requirement => scopes.Contains(requirement.Scope))
				? AuthorizationResult.Success() : AuthorizationResult.Failed()));
		return new(registry, PluginCatalog.Empty(), auth, NullLogger<ApplicationConfigurationController>.Instance)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
	}

	[Test]
	public async Task PrivateDiscoveryCannotBeCachedAcrossAuthorizationChanges()
	{
		var policy = typeof(ApplicationConfigurationController).GetCustomAttributes(typeof(AuthorizeAttribute), true).OfType<AuthorizeAttribute>();
		await Assert.That(policy.Any(attribute => attribute.Policy == "config.admin")).IsTrue();
		var cache = typeof(ApplicationConfigurationController).GetCustomAttributes(typeof(ResponseCacheAttribute), true).OfType<ResponseCacheAttribute>().SingleOrDefault();
		await Assert.That(cache?.NoStore == true).IsTrue();
	}

	[Test]
	[Arguments("aliases: broken")]
	[Arguments("requires_restart: tomorrow")]
	[Arguments("permission: [private]")]
	[Arguments("permission: 42")]
	[Arguments("permission: ''")]
	[Arguments("route: [private]")]
	[Arguments("read_only_reason: 42")]
	[Arguments("label_resource_key: [private]")]
	[Arguments("description_resource_key: false")]
	[Arguments("description: [private]")]
	public async Task MalformedOptionDeclarationsAreRefusedRatherThanSilentlyChanged(string invalid)
	{
		var yaml = $"""
			format: 1.12
			package: example-app
			version: 1.0.0
			kind: application
			application:
			  slug: example
			  display_name: Example
			  schema_url: http/example/schema
			  configuration_tile:
			    id: example
			    title: Example
			    description: Settings
			    route: /apps/example
			    permission: example.admin
			    options:
			      - key: timeout
			        label: Timeout
			        description: Handshake
			        {invalid}
			""";
		await Assert.That(new PackageManifestService().ParseManifest(yaml) is ParsedPackageManifest).IsFalse();
	}

	[Test]
	public async Task DeclaredSecretControlsAreMetadataOnlyAndUseTheirAuthorizedDestination()
	{
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns(Task.FromResult<IReadOnlyList<RegisteredApplication>>([]));
		var result = await Controller(registry, "config.admin").Options();
		var rows = (IReadOnlyList<ConfigurationSearchEntry>)((OkObjectResult)result.Result!).Value!;
		var secret = rows.Single(row => row.Option.Key == "auth.microsoft.clientsecret");
		await Assert.That(secret.Route).IsEqualTo("/admin/config/secrets");
		await Assert.That(secret.Option.RequiresRestart).IsTrue();
		await Assert.That(JsonSerializer.Serialize(secret)).DoesNotContain("Ciphertext");
	}

	[Test]
	public async Task RegisteredOptionsReflectAddUpdateAndRemovalOnEachRequest()
	{
		var registry = new SharpMUSH.Tests.Shared.FakeApplicationRegistry();
		var controller = Controller(registry, "config.admin", "example.admin");
		var tile = new ApplicationConfigurationTile("example", "Example", "Settings", null, "/apps/example", "example.admin")
		{ Options = [new("first", "First", "Original field")] };
		var app = new RegisteredApplication("example", "Example", null, ApplicationKind.Page, "http/example/schema", null, null,
			SharpMUSH.Library.Authorization.PortalRole.Guest, null, null, 0, ConfigurationTile: tile);
		async Task<string[]> Keys() => ((IReadOnlyList<ConfigurationSearchEntry>)((OkObjectResult)(await controller.Options()).Result!).Value!)
			.Where(row => row.Id.StartsWith("app:example:")).Select(row => row.Option.Key).ToArray();
		await registry.UpsertApplicationAsync(app);
		await Assert.That(await Keys()).IsEquivalentTo(new[] { "first" });
		await registry.UpsertApplicationAsync(app with { ConfigurationTile = tile with { Options = [new("replacement", "Replacement", "Updated field")] } });
		await Assert.That(await Keys()).IsEquivalentTo(new[] { "replacement" });
		await registry.RemoveApplicationAsync("example");
		await Assert.That((await Keys()).Length).IsEqualTo(0);
	}

	[Test]
	public async Task ExistingCompiledPluginsRetainSixArgumentTileConstructorAndDeconstruct()
	{
		var constructor = typeof(ApplicationConfigurationTile).GetConstructor(Enumerable.Repeat(typeof(string), 6).ToArray());
		await Assert.That(constructor is not null).IsTrue();
		var tile = (ApplicationConfigurationTile)constructor!.Invoke(["legacy", "Legacy", "Settings", "Settings", "/admin/legacy", "legacy.admin"]);
		await Assert.That(tile.Id).IsEqualTo("legacy");
		await Assert.That(typeof(ApplicationConfigurationTile).GetMethods().Any(method => method.Name == "Deconstruct" && method.GetParameters().Length == 6)).IsTrue();
	}

	[Test]
	public async Task OptionMetadataRoundTripRetainsSemanticRecordEquality()
	{
		const string json = """
			{"id":"example","title":"Example","description":"Settings","route":"/apps/example","permission":"example.admin","options":[{"key":"timeout","label":"Timeout","description":"Handshake","aliases":["connection"]}]}
			""";
		var settings = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		var original = JsonSerializer.Deserialize<ApplicationConfigurationTile>(json, settings)!;
		var restored = JsonSerializer.Deserialize<ApplicationConfigurationTile>(JsonSerializer.Serialize(original, settings), settings)!;
		await Assert.That(restored).IsEqualTo(original);
		await Assert.That(restored.GetHashCode()).IsEqualTo(original.GetHashCode());
	}

	[Test]
	public async Task CoreOptionsRequireConfigurationPermissionBeforeReturningMetadata()
	{
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns(Task.FromResult<IReadOnlyList<RegisteredApplication>>([]));
		var result = await Controller(registry).Options();
		await Assert.That(result.Result is ForbidResult).IsTrue();
	}

	[Test]
	public async Task CoreOptionSearchContainsOnlySchemaMetadataAndCanonicalNames()
	{
		var rows = SharpMUSH.Library.API.SchemaBuilder.BuildSearchMetadata();
		var port = rows.Single(row => row.Option.Key == "Net.Port");
		await Assert.That(port.Option.Aliases!).Contains("port");
		await Assert.That(port.Route).IsEqualTo("/admin/config/net");
		var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		await Assert.That(json).DoesNotContain("defaultValue");
		await Assert.That(json).DoesNotContain("\"configuration\":");
	}

	[Test]
	public async Task SignInOptionsFollowTheProvidersActualEditableFields()
	{
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns(Task.FromResult<IReadOnlyList<RegisteredApplication>>([]));
		var result = await Controller(registry, "config.admin").Options();
		var rows = (IReadOnlyList<ConfigurationSearchEntry>)((OkObjectResult)result.Result!).Value!;
		await Assert.That(rows.Where(row => row.Source == "Microsoft sign-in").Select(row => row.Option.Key)).Contains("AllowedTenants");
		await Assert.That(rows.Where(row => row.Source == "Google sign-in").Select(row => row.Option.Key)).DoesNotContain("AllowedTenants");
		await Assert.That(rows.Any(row => row.Id.StartsWith("app:signin-") && row.Option.Key == "Generation")).IsFalse();
	}

	[Test]
	public async Task DeniedOptionMetadataNeverAppearsInTileDiscovery()
	{
		var tile = JsonSerializer.Deserialize<ApplicationConfigurationTile>("""
			{"id":"example","title":"Example","description":"Settings","route":"/apps/example","permission":"example.admin","options":[{"key":"visible","label":"Visible","description":"Allowed"},{"key":"private","label":"Hidden operation","description":"Confidential metadata","permission":"example.private"}]}
			""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns(Task.FromResult<IReadOnlyList<RegisteredApplication>>([
			new("example", "Example", null, ApplicationKind.Page, "http/example/schema", null, null,
				SharpMUSH.Library.Authorization.PortalRole.Guest, null, null, 0, ConfigurationTile: tile)]));
		var auth = Substitute.For<IAuthorizationService>();
		auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<IEnumerable<IAuthorizationRequirement>>())
			.Returns(call => Task.FromResult(call.Arg<IEnumerable<IAuthorizationRequirement>>().OfType<PermissionRequirement>().All(requirement => requirement.Scope == "example.admin")
				? AuthorizationResult.Success() : AuthorizationResult.Failed()));
		var controller = new ApplicationConfigurationController(registry, PluginCatalog.Empty(), auth,
			NullLogger<ApplicationConfigurationController>.Instance)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		var result = ((OkObjectResult)(await controller.List()).Result!).Value;
		var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		await Assert.That(json).Contains("Allowed");
		await Assert.That(json).DoesNotContain("Confidential metadata");
	}

	[Test]
	public async Task RegisteredTileRetainsOptionMetadataWithoutValues()
	{
		const string json = """
			{"id":"example","title":"Example","description":"Settings","icon":null,"route":"/apps/example","permission":"example.admin","options":[{"key":"timeout","label":"Timeout","description":"Handshake deadline","aliases":["connection"],"permission":"example.advanced"}]}
			""";
		var tile = JsonSerializer.Deserialize<ApplicationConfigurationTile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
		using var output = JsonDocument.Parse(JsonSerializer.Serialize(tile, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
		await Assert.That(output.RootElement.TryGetProperty("options", out var options)).IsTrue();
		await Assert.That(options[0].GetProperty("aliases")[0].GetString()).IsEqualTo("connection");
		await Assert.That(options[0].TryGetProperty("value", out _)).IsFalse();
		await Assert.That(options[0].TryGetProperty("defaultValue", out _)).IsFalse();
	}

	[Test]
	public async Task DescriptionTextIsSearchableWithoutBecomingACommand()
	{
		var row = new PaletteRow("config:timeout", "config:timeout", PaletteRowKind.Go, "Timeout", "", Info: "Handshake deadline", Href: "/apps/example#config-option-timeout");
		var snapshot = new PaletteSnapshot([row], [], null, null, false);
		var rows = PaletteRanker.Search(snapshot, "handshake");
		await Assert.That(rows.Select(value => value.Id)).Contains("config:timeout");
		await Assert.That(rows.Single().Line).IsNull();
	}

	[Test]
	public async Task ManifestRetainsRegisteredOptionMetadata()
	{
		var manifest = new PackageManifestService().ParseManifest("""
			format: 1.12
			package: example-app
			version: 1.0.0
			kind: application
			application:
			  slug: example
			  display_name: Example
			  schema_url: http/example/schema
			  configuration_tile:
			    id: example
			    title: Example
			    description: Settings
			    route: /apps/example
			    permission: example.admin
			    options:
			      - key: timeout
			        label: Timeout
			        description: Handshake deadline
			        aliases: [connection]
			""");
		var parsed = manifest.Expect<ParsedPackageManifest>();
		var json = JsonSerializer.Serialize(parsed.Manifest.Application!.ConfigurationTile, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		await Assert.That(json.ToLowerInvariant()).Contains("handshake");
		var written = PackageManifestWriter.Write(parsed.Manifest);
		var restored = new PackageManifestService().ParseManifest(written).Expect<ParsedPackageManifest>();
		await Assert.That(restored.Manifest.Application!.ConfigurationTile).IsEqualTo(parsed.Manifest.Application.ConfigurationTile);
	}
}
