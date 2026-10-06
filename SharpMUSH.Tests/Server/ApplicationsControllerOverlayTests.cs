using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// Wire-level test: the <c>/api/applications</c> controller, resolving the overlay decorator that production
/// registers, must surface a loaded plugin's contributed application in its DTO list and on a single GET —
/// proving the overlay reaches the public REST surface, not just the service layer.
/// </summary>
public class ApplicationsControllerOverlayTests
{
	private const string PluginSlug = "plugin-widget-demo";

	private static ApplicationsController NewController(IApplicationRegistryService registry) =>
		new(registry, new ThrowingDispatcher(),
			new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create(allowBrowserCode: true)),
			NullLogger<ApplicationsController>.Instance);

	[Test]
	public async Task List_IncludesPluginOverlayApp()
	{
		var inner = new FakeApplicationRegistry();
		await inner.UpsertApplicationAsync(DbApp("db-page"));

		var decorator = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.ForPlugins([new StubApplicationPlugin(PluginApp(PluginSlug))]),
			NullLogger<PluginApplicationRegistryDecorator>.Instance);

		var controller = NewController(decorator);

		var result = await controller.List();
		var ok = result.Result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var dtos = (IReadOnlyList<ApplicationsController.ApplicationDto>)ok!.Value!;

		await Assert.That(dtos.Select(d => d.Slug)).Contains(PluginSlug);
		await Assert.That(dtos.Select(d => d.Slug)).Contains("db-page");

		var pluginDto = dtos.Single(d => d.Slug == PluginSlug);
		await Assert.That(pluginDto.NavPlacement).IsEqualTo("Plugins");
		await Assert.That(pluginDto.Kind).IsEqualTo("Page");
	}

	[Test]
	public async Task Get_ResolvesPluginOverlayApp()
	{
		var inner = new FakeApplicationRegistry();
		var decorator = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.ForPlugins([new StubApplicationPlugin(PluginApp(PluginSlug))]),
			NullLogger<PluginApplicationRegistryDecorator>.Instance);

		var controller = NewController(decorator);

		var result = await controller.Get(PluginSlug);
		var ok = result.Result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var dto = (ApplicationsController.ApplicationDto)ok!.Value!;
		await Assert.That(dto.Slug).IsEqualTo(PluginSlug);
	}

	private static RegisteredApplication DbApp(string slug) =>
		new(slug, slug, null, ApplicationKind.Page, $"http/{slug}/schema", null, null,
			PortalRole.Guest, "Build", null, 1);

	private static RegisteredApplication PluginApp(string slug) =>
		new(slug, "Plugin Widget Demo", "Extension", ApplicationKind.Page, $"http/{slug}/schema",
			$"http/{slug}/data", null, PortalRole.Player, "Plugins", null, 50);

	/// <summary>The controller's read paths never dispatch; this guard fails loudly if that ever changes.</summary>
	private sealed class ThrowingDispatcher : IHttpHandlerCommandDispatcher
	{
		public ValueTask<Found<HttpHandlerResult>> DispatchAsync(
			string method, string path, string body, IEnumerable<(string Name, string Value)> headers,
			CancellationToken ct = default) =>
			throw new InvalidOperationException("read paths must not dispatch");

		public ValueTask<Found<HttpHandlerResult>> DispatchAsync(
			string method, string path, string body, IEnumerable<(string Name, string Value)> headers,
			string clientIp, DBRef? viewer, CancellationToken ct = default) =>
			throw new InvalidOperationException("read paths must not dispatch");
	}
}
