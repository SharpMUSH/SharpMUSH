using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Integration tests for the admin-customized layout collection (sys_layouts) against the active
/// database provider. Verifies upsert/get/list/remove and faithful round-tripping of the nested
/// zone/placement structure stored as a JSON blob.
/// </summary>
public class LayoutRegistryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ILayoutRegistryService Registry =>
		(ILayoutRegistryService)WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.ISharpDatabase>();

	private static LayoutConfiguration SampleLayout() => new(
		new Dictionary<WidgetZone, List<WidgetPlacement>>
		{
			[WidgetZone.MainContent] =
			[
				new WidgetPlacement("CharacterHeader", 0, null),
				new WidgetPlacement("WikiBody", 1, null)
			],
			[WidgetZone.RightSidebar] = [new WidgetPlacement("CharacterGallery", 0, null)]
		},
		new LayoutSettings(LeftSidebarEnabled: true, RightSidebarEnabled: false, FooterEnabled: true));

	[Test, NotInParallel]
	public async Task Layouts_UpsertGetListRemove()
	{
		await Registry.UpsertLayoutAsync("test-profile", SampleLayout());

		var fetched = await Registry.GetLayoutAsync("test-profile");
		await Assert.That(fetched.IsT0).IsTrue();

		var layout = fetched.AsT0;
		await Assert.That(layout.Zones[WidgetZone.MainContent].Count).IsEqualTo(2);
		await Assert.That(layout.Zones[WidgetZone.MainContent][0].WidgetName).IsEqualTo("CharacterHeader");
		await Assert.That(layout.Zones[WidgetZone.MainContent][1].WidgetName).IsEqualTo("WikiBody");
		await Assert.That(layout.Zones[WidgetZone.RightSidebar][0].WidgetName).IsEqualTo("CharacterGallery");
		await Assert.That(layout.Settings.LeftSidebarEnabled).IsTrue();
		await Assert.That(layout.Settings.FooterEnabled).IsTrue();

		// Upsert replaces in full.
		var replacement = new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>> { [WidgetZone.MainContent] = [new WidgetPlacement("WikiIndex", 0, null)] },
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false));
		await Registry.UpsertLayoutAsync("test-profile", replacement);
		var upgraded = await Registry.GetLayoutAsync("test-profile");
		await Assert.That(upgraded.AsT0.Zones[WidgetZone.MainContent].Count).IsEqualTo(1);
		await Assert.That(upgraded.AsT0.Zones[WidgetZone.MainContent][0].WidgetName).IsEqualTo("WikiIndex");

		var scopes = await Registry.GetCustomizedScopesAsync();
		await Assert.That(scopes).Contains("test-profile");

		await Registry.RemoveLayoutAsync("test-profile");
		var missing = await Registry.GetLayoutAsync("test-profile");
		await Assert.That(missing.IsT1).IsTrue();
	}

	[Test, NotInParallel]
	public async Task GetLayout_Missing_ReturnsNotFound()
	{
		var missing = await Registry.GetLayoutAsync("never-customized-scope");
		await Assert.That(missing.IsT1).IsTrue();
	}
}
