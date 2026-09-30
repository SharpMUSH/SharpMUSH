using Bunit;
using Microsoft.Extensions.DependencyInjection;
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
	private static PortalApplication App(string slug, string role) =>
		new(slug, slug, null, "Widget", $"http/{slug}/schema", null, null, role, null, ["MainContent"], 1);

	private IRenderedComponent<ZoneRenderer> RenderWith(params PortalApplication[] apps)
	{
		var registry = new WidgetRegistry();
		foreach (var app in apps) registry.Register(new ApplicationPortalWidget(app));
		Services.AddSingleton<IWidgetRegistry>(registry);
		// SchemaWidget's own services; it is rendered, its routes are never answered.
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(new NoAnswer()) { BaseAddress = new Uri("https://localhost/") });
		Services.AddSingleton(new SchemaAppService(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<SchemaAppService>.Instance));
		Services.AddSingleton(new ApplicationCatalog(apps));
		Services.AddSingleton(new ApplicationRegistryClient(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new CharacterDirectoryService(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<CharacterDirectoryService>.Instance));
		var layout = EmptyLayout();
		layout.Zones[WidgetZone.MainContent] = apps.Select((a, i) => new WidgetPlacement(a.Slug, i, null)).ToList();
		return Render<ZoneRenderer>(p => p.Add(c => c.Zone, WidgetZone.MainContent).Add(c => c.Layout, layout));
	}

	private sealed class NoAnswer : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
	}

	private static List<string> Rendered(IRenderedComponent<ZoneRenderer> cut) =>
		cut.FindComponents<WidgetErrorBoundary>().Select(b => b.Instance.WidgetName).ToList();

	[Test]
	public async Task AnAnonymousVisitor_DoesNotGetAStaffOnlyApp()
	{
		AddAuthorization();
		var cut = RenderWith(App("weather", "Guest"), App("staffboard", "Wizard"));
		await Assert.That(Rendered(cut)).IsEquivalentTo(new[] { "weather" });
	}

	[Test]
	public async Task AWizard_GetsIt()
	{
		AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");
		var cut = RenderWith(App("weather", "Guest"), App("staffboard", "Wizard"));
		await Assert.That(Rendered(cut)).IsEquivalentTo(new[] { "weather", "staffboard" });
	}
}
