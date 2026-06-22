using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
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

	private static RegisteredApplication PageApp(string slug, int order = 0) => new(
		slug, $"App {slug}", "Icons.Material.Filled.Apps", ApplicationKind.Page,
		$"http/{slug}/schema", $"http/{slug}", $"http/{slug}/submit", PortalRole.Player, "main", null, order);

	private static RegisteredApplication WidgetApp(string slug) => new(
		slug, $"Widget {slug}", null, ApplicationKind.Widget,
		$"http/{slug}/schema", null, null, PortalRole.Wizard, null,
		[WidgetZone.MainContent, WidgetZone.RightSidebar], 5);

	[Test, NotInParallel]
	public async Task Applications_UpsertGetListRemove()
	{
		await Registry.UpsertApplicationAsync(PageApp("app-alpha", order: 2));
		await Registry.UpsertApplicationAsync(PageApp("app-beta", order: 1));

		var fetched = await Registry.GetApplicationAsync("app-alpha");
		await Assert.That(fetched.IsT0).IsTrue();
		await Assert.That(fetched.AsT0).IsEqualTo(PageApp("app-alpha", order: 2));

		// Upsert replaces in full.
		await Registry.UpsertApplicationAsync(PageApp("app-alpha", order: 9));
		var upgraded = await Registry.GetApplicationAsync("app-alpha");
		await Assert.That(upgraded.AsT0.Order).IsEqualTo(9);

		// List is ordered by Order then slug.
		var all = await Registry.GetApplicationsAsync();
		var ours = all.Where(a => a.Slug.StartsWith("app-")).ToList();
		await Assert.That(ours.Count).IsEqualTo(2);
		await Assert.That(ours[0].Slug).IsEqualTo("app-beta"); // order 1 before 9

		await Registry.RemoveApplicationAsync("app-alpha");
		await Registry.RemoveApplicationAsync("app-beta");
		var missing = await Registry.GetApplicationAsync("app-alpha");
		await Assert.That(missing.IsT1).IsTrue();
	}

	[Test, NotInParallel]
	public async Task WidgetApplication_RoundTripsEnumsAndZones()
	{
		await Registry.UpsertApplicationAsync(WidgetApp("app-widget"));

		var fetched = await Registry.GetApplicationAsync("app-widget");
		await Assert.That(fetched.IsT0).IsTrue();

		var app = fetched.AsT0;
		await Assert.That(app.Kind).IsEqualTo(ApplicationKind.Widget);
		await Assert.That(app.MinimumRole).IsEqualTo(PortalRole.Wizard);
		await Assert.That(app.DataUrl).IsNull();
		await Assert.That(app.Zones).IsNotNull();
		await Assert.That(app.Zones!.Count).IsEqualTo(2);
		await Assert.That(app.Zones).Contains(WidgetZone.MainContent);
		await Assert.That(app.Zones).Contains(WidgetZone.RightSidebar);

		await Registry.RemoveApplicationAsync("app-widget");
	}

	[Test, NotInParallel]
	public async Task ComponentApplication_RoundTripsRenderKindAndComponentFields()
	{
		var component = new RegisteredApplication(
			"app-component", "Component App", null, ApplicationKind.Page,
			"http/app-component/schema", null, null, PortalRole.Player, "Plugins", null, 3,
			OwningPackage: "demo-pkg",
			RenderKind: ApplicationRenderKind.Component,
			ComponentAssemblyUrl: "api/plugins/demo-pkg/ui/Demo.Ui.dll",
			ComponentTypeName: "Demo.Ui.Widget");

		await Registry.UpsertApplicationAsync(component);

		var fetched = await Registry.GetApplicationAsync("app-component");
		await Assert.That(fetched.IsT0).IsTrue();
		var app = fetched.AsT0;
		await Assert.That(app.RenderKind).IsEqualTo(ApplicationRenderKind.Component);
		await Assert.That(app.ComponentAssemblyUrl).IsEqualTo("api/plugins/demo-pkg/ui/Demo.Ui.dll");
		await Assert.That(app.ComponentTypeName).IsEqualTo("Demo.Ui.Widget");

		await Registry.RemoveApplicationAsync("app-component");
	}

	[Test, NotInParallel]
	public async Task GetApplication_Missing_ReturnsNotFound()
	{
		var missing = await Registry.GetApplicationAsync("does-not-exist");
		await Assert.That(missing.IsT1).IsTrue();
	}
}
