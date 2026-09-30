using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Components.Layout;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// A widget application placed in any layout renders only for viewers who meet its minimum role. The layout
/// is one shared arrangement per scope, so the gate has to be where it renders, not where it was placed.
/// </summary>
public class ZoneRendererRoleTests : ZoneRendererTestBase
{
	private ApplicationsBySlug? _api;

	private static PortalApplication App(string slug, string role) =>
		new(slug, slug, null, "Widget", $"http/{slug}/schema", null, null, role, null, ["MainContent"], 1);

	/// <summary>
	/// Renders a zone placing <paramref name="apps"/>. With <paramref name="catalogued"/> the startup catalog
	/// had them and each is a registered <see cref="ApplicationPortalWidget"/>; without it the catalog is
	/// still empty (pending or failed), so each placement is the registry's by-slug fallback and the app is
	/// only reachable through the per-slug fetch.
	/// </summary>
	private IRenderedComponent<ZoneRenderer> RenderWith(bool catalogued, params PortalApplication[] apps)
	{
		var registry = new WidgetRegistry();
		if (catalogued)
		{
			foreach (var app in apps) registry.Register(new ApplicationPortalWidget(app));
		}

		Services.AddSingleton<IWidgetRegistry>(registry);
		_api = new ApplicationsBySlug(apps);
		var api = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(api);
		Services.AddSingleton(new SchemaAppService(factory, NullLogger<SchemaAppService>.Instance));
		Services.AddSingleton(new ApplicationCatalog(catalogued ? apps : []));
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance));
		var layout = EmptyLayout();
		layout.Zones[WidgetZone.MainContent] = apps.Select((a, i) => new WidgetPlacement(a.Slug, i, null)).ToList();
		return Render<ZoneRenderer>(p => p.Add(c => c.Zone, WidgetZone.MainContent).Add(c => c.Layout, layout));
	}

	/// <summary>Answers <c>api/applications/{slug}</c> for the given apps; every other route is not found.</summary>
	private sealed class ApplicationsBySlug(IReadOnlyList<PortalApplication> apps) : HttpMessageHandler
	{
		public System.Collections.Concurrent.ConcurrentQueue<string> Requested { get; } = new();

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requested.Enqueue(request.RequestUri!.AbsolutePath);
			return Task.FromResult(Respond(request));
		}

		/// <summary>The response is the caller's to dispose; <see cref="HttpClient"/> hands it on.</summary>
		private HttpResponseMessage Respond(HttpRequestMessage request) =>
			apps.FirstOrDefault(a => request.RequestUri!.AbsolutePath == $"/api/applications/{a.Slug}") is { } app
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(app) }
				: new HttpResponseMessage(HttpStatusCode.NotFound);
	}

	private static List<string> Rendered(IRenderedComponent<ZoneRenderer> cut) =>
		cut.FindComponents<WidgetErrorBoundary>().Select(b => b.Instance.WidgetName).ToList();

	[Test]
	public async Task AnAnonymousVisitor_DoesNotGetAStaffOnlyApp()
	{
		AddAuthorization();
		var cut = RenderWith(catalogued: true, App("weather", "Guest"), App("staffboard", "Wizard"));
		await Assert.That(Rendered(cut)).IsEquivalentTo(new[] { "weather" });
	}

	[Test]
	public async Task AWizard_GetsIt()
	{
		AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");
		var cut = RenderWith(catalogued: true, App("weather", "Guest"), App("staffboard", "Wizard"));
		await Assert.That(Rendered(cut)).IsEquivalentTo(new[] { "weather", "staffboard" });
	}

	/// <summary>
	/// Before the catalog is in, the registry hands back its by-slug fallback, which carries no role. The
	/// placement waits for its application instead of rendering ungated.
	/// </summary>
	[Test]
	public async Task BeforeTheCatalogIsIn_AnAnonymousVisitor_StillDoesNotGetAStaffOnlyApp()
	{
		AddAuthorization();
		var cut = RenderWith(catalogued: false, App("weather", "Guest"), App("staffboard", "Wizard"));
		cut.WaitForState(() => _api!.Requested.Contains("/api/applications/staffboard"));
		cut.WaitForState(() => Rendered(cut).Contains("weather"));
		await Assert.That(Rendered(cut)).IsEquivalentTo(new[] { "weather" });
	}

	[Test]
	public async Task BeforeTheCatalogIsIn_AWizard_GetsIt()
	{
		AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");
		var cut = RenderWith(catalogued: false, App("weather", "Guest"), App("staffboard", "Wizard"));
		cut.WaitForState(() => Rendered(cut).Count == 2);
		await Assert.That(Rendered(cut)).IsEquivalentTo(new[] { "weather", "staffboard" });
	}
}
