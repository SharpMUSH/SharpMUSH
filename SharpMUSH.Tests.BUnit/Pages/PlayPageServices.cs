using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Components;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// What /play needs beyond the terminal and the account: the comm feed, the section sidebar's collapse
/// memory, the scene hub and the REST services the story, the character sheet and its Details read.
/// Each HTTP service resolves the test's own <see cref="IHttpClientFactory"/> when first used, so a test
/// may register that after calling this.
/// </summary>
internal static class PlayPageServices
{
	public static FakeSceneHub Install(IServiceCollection services, ICommFeed? comms = null, IReadOnlyList<PortalApplication>? apps = null)
	{
		var hub = new FakeSceneHub();
		services
			.AddSingleton(comms ?? new TestCommFeed())
			.AddSingleton<IChannelWho>(new ChannelWhoFeed(new OobChannelStore()))
			.AddSingleton<SidebarCollapseService>()
			.AddSingleton<IConnectionStateService>(hub)
			.AddSingleton<ISceneHubControl>(hub)
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton(sp => new SceneService(sp.GetRequiredService<IHttpClientFactory>(), TestAccountAuth.Of(sp)))
			.AddSingleton(sp => new CharacterProfileService(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<CharacterDirectoryService>()))
			.AddSingleton(sp => new GalleryService(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<CharacterDirectoryService>()))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(new ApplicationCatalog(apps ?? []))
			.AddSingleton<ILayoutService>(sp => new LayoutService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<LayoutService>.Instance))
			.AddSingleton<IWidgetRegistry>(_ =>
			{
				var registry = new WidgetRegistry();
				foreach (var widget in SharpMUSH.Client.Widgets.BuiltInWidgets.All) registry.Register(widget);
				foreach (var app in apps ?? []) registry.Register(new SharpMUSH.Client.Widgets.ApplicationPortalWidget(app));
				return registry;
			});
		return hub;
	}
}
