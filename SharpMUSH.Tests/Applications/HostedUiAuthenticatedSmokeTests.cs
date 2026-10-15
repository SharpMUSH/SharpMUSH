using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Plugins;
using SharpMUSH.Tests.Server;
using SharpMUSH.Tests.Services;
using SharpMUSH.Tests.Shared;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Applications;

/// <summary>Real production account-session authentication over TestServer; all world/account backing data is synthetic and disposable.</summary>
public class HostedUiAuthenticatedSmokeTests
{
	[Test]
	public async Task ProductionSessionMiddlewareGatesBootstrapAndReadOnlyGraphWithoutListenerOrPersistentCredentials()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		using var cache = new FusionCache(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new()));
		try
		{
			var folder = Path.Combine(directories.UiPackages, "graph-proof"); Directory.CreateDirectory(folder);
			var definitionBytes = System.Text.Encoding.UTF8.GetBytes(UiDefinitionTests.DefinitionJson);
			var html = System.Text.Encoding.UTF8.GetBytes("<!doctype html><h1>Graph</h1>");
			await File.WriteAllBytesAsync(Path.Combine(folder, "ui.json"), definitionBytes);
			await File.WriteAllBytesAsync(Path.Combine(folder, "index.html"), html);
			await File.WriteAllTextAsync(Path.Combine(folder, PluginUiBinaryManifest.FileName), new PluginUiBinaryManifest(new Dictionary<string, string>
			{ ["ui.json"] = PluginPackageFixture.Sha256Of(definitionBytes), ["index.html"] = PluginPackageFixture.Sha256Of(html) }).ToJson());
			var registry = new FakeApplicationRegistry();
			await registry.UpsertApplicationAsync(new("east", "East", null, ApplicationKind.Page, "", null, null, PortalRole.Player, null, null, 0,
				RenderKind: ApplicationRenderKind.Hosted, DefinitionId: "graph-proof", Config: System.Text.Json.JsonSerializer.SerializeToElement(new { rooms = new[] { 10, 20 } })));
			var objects = new TestObjectFactory(); var a = objects.CreateRoom(10, "A"); var b = objects.CreateRoom(20, "B");
			var player = objects.CreatePlayer(101, "Test builder", a).Expect<SharpPlayer>();
			var exit = objects.CreateExit(11, "one way", [], a, b).Expect<SharpExit>();
			var mediator = Substitute.For<IMediator>();
			mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<AnyOptionalSharpObject>(
				objects.GetAllRooms().Select(room => new AnySharpObject(room)).Concat(objects.GetAllObjects())
					.FirstOrDefault(o => o.Object().DBRef.Number == call.Arg<GetObjectNodeQuery>().DBRef.Number) is { } found ? found : new None()));
			mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>()).Returns(new[] { exit }.ToAsyncEnumerable());
			var permissions = Substitute.For<IPermissionService>(); var canReadB = true;
			permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(call => ValueTask.FromResult(call.ArgAt<AnySharpObject>(1).Object().DBRef.Number != 20 || canReadB));
			var reality = Substitute.For<IRealityPolicy>(); reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
			var attributes = Substitute.For<IAttributeService>();
			attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "_LINKTYPE", IAttributeService.AttributeMode.Read, false).Returns(new None());
			var accounts = Substitute.For<IAccountService>();
			accounts.GetByIdAsync("test-account").Returns(new SharpAccount { Id = "test-account", Username = "Test account", Email = null, PasswordHash = string.Empty, CreatedAt = 0, Status = AccountStatus.Active });
			var linked = true;
			accounts.GetCharactersAsync("test-account").Returns(_ => ValueTask.FromResult<IReadOnlyList<SharpPlayer>>(linked ? new[] { player } : []));
			var capabilities = Substitute.For<IAdministrativeCapabilityService>();
			capabilities.GetContextAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new PermissionContext(BuiltInRoles.All.Where(r => r.Slug == BuiltInRoles.PlayerSlug).ToArray(), new Dictionary<string, PermissionState>(), false));
			capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
			capabilities.GetGameActorAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<CapabilityActor?>(linked ? new("test-account", player.Object.DBRef, player.Object.DBRef) : null));
			var options = new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create(allowBrowserCode: true) with { SitelockRules = new SitelockRulesOptions([]) });
			var sessions = new InMemoryAccountSessionStore();
			// Short-lived memory-only token. Never written, printed, configured or used outside TestServer.
			var token = await sessions.CreateTokenAsync("test-account", TimeSpan.FromMinutes(1), "unknown", player.Object.Key, player.Object.CreationTime);
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
			builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
			builder.Services.AddSingleton<IOptionsWrapper<SharpMUSHOptions>>(options);
			builder.Services.AddSingleton(directories); builder.Services.AddSingleton<UiPackageStore>();
			builder.Services.AddSingleton<IApplicationRegistryService>(registry); builder.Services.AddSingleton(mediator);
			builder.Services.AddSingleton(permissions); builder.Services.AddSingleton(reality); builder.Services.AddSingleton(attributes);
			builder.Services.AddSingleton<IEngineCommandInvoker>(Substitute.For<IEngineCommandInvoker>());
			builder.Services.AddSingleton<IConnectionService>(Substitute.For<IConnectionService>());
			builder.Services.AddSingleton(capabilities); builder.Services.AddSingleton<IVisibleWorldProjection, VisibleWorldProjection>();
			builder.Services.AddSingleton(accounts); builder.Services.AddSingleton<IAccountSessionStore>(sessions);
			builder.Services.AddSingleton(new AccountClaimsService(capabilities, cache, new AccountClaimsInvalidator(cache, new SharpMUSH.Library.Behaviors.ObjectVersions()), NullLogger<AccountClaimsService>.Instance));
			builder.Services.AddSingleton<SitelockGuard>(); builder.Services.AddHttpContextAccessor();
			builder.Services.AddTransient<IClaimsTransformation, FreshPermissionClaimsTransformation>();
			builder.Services.AddAuthentication(AccountSessionAuthenticationHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, AccountSessionAuthenticationHandler>(AccountSessionAuthenticationHandler.SchemeName, _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllers().AddApplicationPart(typeof(HostedApplicationsController).Assembly);
			await using var app = builder.Build(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
			using var client = app.GetTestClient(); client.BaseAddress = new Uri("https://localhost/");
			using (var response = await client.GetAsync("api/applications/east/host")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
			using (var response = await client.GetAsync("api/ui/graph-proof/assets/index.html"))
			{
				await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
				await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/html");
			}
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using (var response = await client.GetAsync("api/applications/east/host")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			var existing = (await registry.GetApplicationAsync("east")).Expect<RegisteredApplication>();
			await registry.UpsertApplicationAsync(existing with { Permission = "maps.read" });
			using (var response = await client.GetAsync("api/applications/east/host")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
			await registry.UpsertApplicationAsync(existing with { ConfigVersion = 2 });
			using (var response = await client.GetAsync("api/applications/east/host")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
			await registry.UpsertApplicationAsync(existing);
			using (var response = await client.GetAsync("api/ui/graph-proof/assets/ui.json")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
			using (var response = await client.GetAsync("api/ui/graph-proof/assets/%2e%2e%2fui.json")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
			using (var response = await client.GetAsync("api/objects/topology?rooms=-1")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
			using (var response = await client.GetAsync("api/objects/topology?" + string.Join("&", Enumerable.Repeat("rooms=10", 101)))) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
			var graph = await client.GetFromJsonAsync<TopologyDto>("api/objects/topology?rooms=10&rooms=20");
			await Assert.That(graph!.Exits[0].Destination).IsEqualTo("#20");
			canReadB = false;
			graph = await client.GetFromJsonAsync<TopologyDto>("api/objects/topology?rooms=10&rooms=20");
			await Assert.That(graph!.Rooms.Count).IsEqualTo(1); await Assert.That(graph.Exits[0].Destination).IsNull();
			linked = false;
			using (var response = await client.GetAsync("api/objects/topology?rooms=10")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
			await sessions.RevokeAsync(token);
			using (var response = await client.GetAsync("api/applications/east/host")) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		}
		finally { PluginPackageFixture.Delete(directories); }
	}
}
