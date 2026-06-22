using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

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
		new(registry, new ThrowingDispatcher(), NullLogger<ApplicationsController>.Instance);

	[Test]
	public async Task List_IncludesPluginOverlayApp()
	{
		var inner = new FakeRegistry();
		await inner.UpsertApplicationAsync(DbApp("db-page"));

		var decorator = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.ForPlugins([new StubAppPlugin(PluginApp(PluginSlug))]),
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
		var inner = new FakeRegistry();
		var decorator = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.ForPlugins([new StubAppPlugin(PluginApp(PluginSlug))]),
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

	private sealed class StubAppPlugin(params RegisteredApplication[] apps)
		: SharpMUSH.Library.Plugins.IPlugin, SharpMUSH.Library.Plugins.IApplicationSource
	{
		public string Id => "stub-app";
		public string Version => "1.0.0";
		public IReadOnlyList<string> Dependencies => [];
		public int Priority => 0;
		public void Initialize(IServiceProvider services) { }
		public IEnumerable<RegisteredApplication> GetApplications() => apps;
	}

	private sealed class FakeRegistry : IApplicationRegistryService
	{
		private readonly Dictionary<string, RegisteredApplication> _store = new(StringComparer.OrdinalIgnoreCase);
		public Task UpsertApplicationAsync(RegisteredApplication application) { _store[application.Slug] = application; return Task.CompletedTask; }
		public Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug) =>
			Task.FromResult(_store.TryGetValue(slug, out var app) ? (OneOf<RegisteredApplication, NotFound>)app : new NotFound());
		public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync() =>
			Task.FromResult<IReadOnlyList<RegisteredApplication>>(_store.Values.OrderBy(a => a.Order).ToList());
		public Task RemoveApplicationAsync(string slug) { _store.Remove(slug); return Task.CompletedTask; }
	}

	/// <summary>The controller's read paths never dispatch; this guard fails loudly if that ever changes.</summary>
	private sealed class ThrowingDispatcher : IHttpHandlerCommandDispatcher
	{
		public ValueTask<OneOf<HttpHandlerResult, NotFound>> DispatchAsync(
			string method, string path, string body, IEnumerable<(string Name, string Value)> headers,
			CancellationToken ct = default) =>
			throw new InvalidOperationException("read paths must not dispatch");
	}
}
