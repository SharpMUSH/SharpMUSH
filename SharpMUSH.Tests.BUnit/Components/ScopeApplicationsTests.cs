using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Components.Characters;
using SharpMUSH.Client.Components.Layout;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Widget applications that join a page by their scope rather than by a layout edit: a scoped zone takes the
/// scope's applications for the zone each names first, and a name card draws the <c>card</c> scope's, both
/// for the viewer's role.
/// </summary>
public class ScopeApplicationsTests : ZoneRendererTestBase
{
	private static PortalApplication App(string slug, string scope, string role = "Guest", params string[] zones) =>
		new(slug, slug, null, "Widget", $"http/{slug}/schema", $"http/{slug}/data?objid={{objid}}", null, role, null,
			zones.Length == 0 ? ["MainContent"] : zones, 1, Scope: scope);

	private static readonly LayoutConfiguration ProfileLayout = new(
		new Dictionary<WidgetZone, List<WidgetPlacement>>
		{
			[WidgetZone.MainContent] = [new WidgetPlacement("placed", 0, null)],
			[WidgetZone.RightSidebar] = [],
		},
		new LayoutSettings(false, false));

	/// <summary>Registers <paramref name="apps"/> the way startup does once the catalog is in; every route answers 404.</summary>
	private void Install(params PortalApplication[] apps) => Install(new NotFoundHandler(), apps);

	/// <summary>Registers <paramref name="apps"/> the way startup does once the catalog is in; <paramref name="routes"/> answers.</summary>
	private void Install(HttpMessageHandler routes, params PortalApplication[] apps)
	{
		var registry = new WidgetRegistry();
		foreach (var app in apps) registry.Register(new ApplicationPortalWidget(app));
		Services.AddSingleton<IWidgetRegistry>(registry);
		var api = Track(new HttpClient(routes) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(api);
		Services.AddSingleton(new SchemaAppService(factory, NullLogger<SchemaAppService>.Instance));
		Services.AddSingleton(new ApplicationCatalog(apps));
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance));
		Services.AddSingleton(Substitute.For<IPlayTerminalService>());
		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(LayoutScopes.Profile).Returns(Task.FromResult(ProfileLayout));
		Services.AddSingleton(layouts);
	}

	private sealed class NotFoundHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}

	/// <summary>Every route answers a view with no pages: an application with nothing to show this viewer.</summary>
	private sealed class EmptyViewHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"kind":"view","schema_version":1,"pages":[]}""", System.Text.Encoding.UTF8, "application/json")
			});
	}

	private static List<string> Rendered(IRenderedComponent<IComponent> cut) =>
		cut.FindComponents<WidgetErrorBoundary>().Select(b => b.Instance.WidgetName).ToList();

	private IRenderedComponent<ScopedZone> RenderZone(WidgetZone zone) =>
		Render<ScopedZone>(p => p.Add(x => x.Scope, LayoutScopes.Profile).Add(x => x.Zone, zone));

	[Test]
	public async Task A_scope_application_with_nothing_to_show_draws_nothing()
	{
		AddAuthorization();
		Install(new EmptyViewHandler(), App("checklist", "profile"));

		var cut = RenderZone(WidgetZone.MainContent);

		cut.WaitForState(() => Rendered(cut).Contains("checklist") && !cut.Markup.Contains("mud-progress-circular"));
		await Assert.That(cut.Markup).DoesNotContain("WidNothingToDisplay");
	}

	[Test]
	public async Task A_scope_application_joins_the_zone_after_the_placed_widgets()
	{
		AddAuthorization();
		Install(App("placed", "profile"), App("relationships", "profile"));

		var cut = RenderZone(WidgetZone.MainContent);

		cut.WaitForState(() => Rendered(cut).Count == 2);
		await Assert.That(string.Join(",", Rendered(cut))).IsEqualTo("placed,relationships");
	}

	[Test]
	public async Task It_joins_only_the_first_of_its_zones_that_the_scope_draws()
	{
		AddAuthorization();
		Install(App("placed", "profile"), App("relationships", "profile", "Guest", "TopBar", "RightSidebar", "MainContent"));

		var main = RenderZone(WidgetZone.MainContent);
		var aside = RenderZone(WidgetZone.RightSidebar);

		aside.WaitForState(() => Rendered(aside).Count == 1);
		await Assert.That(Rendered(aside)).IsEquivalentTo(new[] { "relationships" });
		await Assert.That(Rendered(main)).IsEquivalentTo(new[] { "placed" });
	}

	[Test]
	public async Task An_application_of_another_scope_or_above_the_viewers_role_stays_out()
	{
		AddAuthorization();
		Install(App("placed", "profile"), App("playpanel", "play"), App("staffnotes", "profile", "Wizard"));

		var cut = RenderZone(WidgetZone.MainContent);

		cut.WaitForState(() => Rendered(cut).Count >= 1);
		await Assert.That(Rendered(cut)).IsEquivalentTo(new[] { "placed" });
	}

	[Test]
	public async Task A_name_card_draws_the_card_scope_applications_the_viewer_may_see()
	{
		AddAuthorization().SetAuthorized("moth").SetRoles("Player");
		Install(App("relationship-card", "card"), App("staff-card", "card", "Wizard"), App("relationships", "profile"));

		var cut = Render<CardApplications>(p => p.Add(x => x.CharacterName, "Wren"));

		cut.WaitForState(() => cut.FindComponents<SchemaWidget>().Count > 0);
		await Assert.That(cut.FindComponents<SchemaWidget>().Select(w => w.Instance.WidgetName)).IsEquivalentTo(new[] { "relationship-card" });
	}

	private IRenderedComponent<NameCard> RenderNameCard() =>
		Render<NameCard>(p => p
			.Add(x => x.CharacterName, "Wren")
			.Add(x => x.OpenDelay, TimeSpan.Zero)
			.Add(x => x.CloseDelay, TimeSpan.Zero)
			.AddChildContent("<a class=\"story-name\" href=\"/character/Wren\">Wren</a>"));

	[Test]
	public async Task Resting_on_a_name_opens_its_card_and_leaving_closes_it()
	{
		AddAuthorization().SetAuthorized("moth").SetRoles("Player");
		Install(App("relationship-card", "card"));
		// The card is drawn by the page's popover provider, so that is where it is found.
		var popovers = Render<MudBlazor.MudPopoverProvider>();
		var cut = RenderNameCard();

		await Assert.That(popovers.FindComponents<CardApplications>().Count).IsEqualTo(0);

		await cut.Find("span.name-card").TriggerEventAsync("onmouseenter", new MouseEventArgs());
		popovers.WaitForState(() => popovers.FindComponents<CardApplications>().Count == 1);
		await Assert.That(popovers.FindComponents<CardApplications>()[0].Instance.CharacterName).IsEqualTo("Wren");
		await Assert.That(popovers.Find(".name-card-name").GetAttribute("href")).IsEqualTo("/character/Wren");

		await cut.Find("span.name-card").TriggerEventAsync("onmouseleave", new MouseEventArgs());
		popovers.WaitForState(() => popovers.FindComponents<CardApplications>().Count == 0);
	}

	[Test]
	public async Task Escape_closes_an_open_card()
	{
		AddAuthorization();
		Install();
		var popovers = Render<MudBlazor.MudPopoverProvider>();
		var cut = RenderNameCard();

		await cut.Find("span.name-card").TriggerEventAsync("onmouseenter", new MouseEventArgs());
		popovers.WaitForState(() => popovers.FindComponents<CardApplications>().Count == 1);
		await cut.Find("span.name-card").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

		popovers.WaitForState(() => popovers.FindComponents<CardApplications>().Count == 0);
	}
}
