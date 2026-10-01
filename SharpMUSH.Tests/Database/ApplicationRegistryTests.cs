using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Integration tests for the Dynamic Application registry collection (sys_applications, Area 21)
/// against the active database provider. Verifies upsert/get/list/remove plus faithful round-tripping
/// of the enum and zone-list fields.
/// </summary>
public class ApplicationRegistryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IApplicationRegistryService Registry =>
		(IApplicationRegistryService)WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.ISharpDatabase>();

	private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

	private static RegisteredApplication PageApp(string slug, int order = 0) => new(
		slug, $"App {slug}", "Icons.Material.Filled.Apps", ApplicationKind.Page,
		$"http/{slug}/schema", $"http/{slug}", $"http/{slug}/submit", PortalRole.Player, "main", null, order);

	private static RegisteredApplication WidgetApp(string slug) => new(
		slug, $"Widget {slug}", null, ApplicationKind.Widget,
		$"http/{slug}/schema", null, null, PortalRole.Wizard, null,
		[WidgetZone.MainContent, WidgetZone.RightSidebar], 5);

	[Test]
	public async Task Applications_UpsertGetListRemove()
	{
		// The registry is shared by the session, so the listing is narrowed to this test's own slugs.
		var prefix = Slug("app");
		var alpha = $"{prefix}-alpha";
		var beta = $"{prefix}-beta";
		await Registry.UpsertApplicationAsync(PageApp(alpha, order: 2));
		await Registry.UpsertApplicationAsync(PageApp(beta, order: 1));

		var fetched = await Registry.GetApplicationAsync(alpha);
		await Assert.That(fetched.Value).IsEqualTo(PageApp(alpha, order: 2));

		// Upsert replaces in full.
		await Registry.UpsertApplicationAsync(PageApp(alpha, order: 9));
		var upgraded = await Registry.GetApplicationAsync(alpha);
		await Assert.That(upgraded.Expect<RegisteredApplication>().Order).IsEqualTo(9);

		// List is ordered by Order then slug.
		var all = await Registry.GetApplicationsAsync();
		var ours = all.Where(a => a.Slug.StartsWith($"{prefix}-")).ToList();
		await Assert.That(ours.Count).IsEqualTo(2);
		await Assert.That(ours[0].Slug).IsEqualTo(beta); // order 1 before 9

		await Registry.RemoveApplicationAsync(alpha);
		await Registry.RemoveApplicationAsync(beta);
		var missing = await Registry.GetApplicationAsync(alpha);
		await Assert.That(missing.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task WidgetApplication_RoundTripsEnumsAndZones()
	{
		var slug = Slug("app-widget");
		await Registry.UpsertApplicationAsync(WidgetApp(slug));

		var app = (await Registry.GetApplicationAsync(slug)).Expect<RegisteredApplication>();
		await Assert.That(app.Kind).IsEqualTo(ApplicationKind.Widget);
		await Assert.That(app.MinimumRole).IsEqualTo(PortalRole.Wizard);
		await Assert.That(app.DataUrl).IsNull();
		await Assert.That(app.Zones).IsNotNull();
		await Assert.That(app.Zones!.Count).IsEqualTo(2);
		await Assert.That(app.Zones).Contains(WidgetZone.MainContent);
		await Assert.That(app.Zones).Contains(WidgetZone.RightSidebar);

		await Registry.RemoveApplicationAsync(slug);
	}

	[Test]
	public async Task ComponentApplication_RoundTripsRenderKindAndComponentFields()
	{
		var slug = Slug("app-component");
		var component = new RegisteredApplication(
			slug, "Component App", null, ApplicationKind.Page,
			"http/app-component/schema", null, null, PortalRole.Player, "Plugins", null, 3,
			OwningPackage: "demo-pkg",
			RenderKind: ApplicationRenderKind.Component,
			ComponentAssemblyUrl: "api/plugins/demo-pkg/ui/Demo.Ui.dll",
			ComponentTypeName: "Demo.Ui.Widget");

		await Registry.UpsertApplicationAsync(component);

		var fetched = await Registry.GetApplicationAsync(slug);
		var app = fetched.Expect<RegisteredApplication>();
		await Assert.That(app.RenderKind).IsEqualTo(ApplicationRenderKind.Component);
		await Assert.That(app.ComponentAssemblyUrl).IsEqualTo("api/plugins/demo-pkg/ui/Demo.Ui.dll");
		await Assert.That(app.ComponentTypeName).IsEqualTo("Demo.Ui.Widget");

		await Registry.RemoveApplicationAsync(slug);
	}

	[Test]
	public async Task GetApplication_Missing_ReturnsNotFound()
	{
		var missing = await Registry.GetApplicationAsync("does-not-exist");
		await Assert.That(missing.Value).IsTypeOf<NotFound>();
	}
}
