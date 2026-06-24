using Microsoft.Extensions.Logging.Abstractions;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Proves the <see cref="IApplicationSource"/> seam and the registry-overlay decorator end-to-end:
/// <list type="bullet">
///   <item>the real fixture plugin DLL — built and copied into <c>plugins-unit/app-source/</c> — is loaded
///   from disk through <see cref="PluginLoaderService"/>, classified <b>load-once</b> (it contributes UI),
///   and its <see cref="IApplicationSource.GetApplications"/> cast cleanly across the isolation boundary;</item>
///   <item>the decorator unions the plugin's app into reads when the catalog has the source and omits it when
///   empty; DB-backed apps still upsert/get/remove; and the slug-collision rule (DB wins, plugin skipped)
///   holds.</item>
/// </list>
/// </summary>
[NotInParallel]
public class ApplicationSourceOverlayTests
{
	private static string AppSourceDllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "app-source", "ApplicationSourcePlugin.dll");

	private const string PluginSlug = "plugin-widget-demo";

	[Test]
	public async Task FixturePlugin_LoadsFromDisk_ContributesApplication_AndIsLoadOnce()
	{
		await Assert.That(File.Exists(AppSourceDllPath))
			.IsTrue()
			.Because($"the ApplicationSourcePlugin fixture DLL must be copied to {AppSourceDllPath}");

		var loaded = PluginLoaderService.LoadOne(AppSourceDllPath, NullLogger.Instance);
		await Assert.That(loaded).IsNotNull();

		await Assert.That(loaded!.IsUnloadable).IsFalse()
			.Because("a plugin contributing IApplicationSource is a load-once seam");
		await Assert.That(PluginLoaderService.IsUnloadablePlugin(loaded.Plugin)).IsFalse();

		await Assert.That(loaded.Plugin is IApplicationSource).IsTrue();
		var apps = ((IApplicationSource)loaded.Plugin).GetApplications().ToList();
		await Assert.That(apps.Count).IsEqualTo(1);
		await Assert.That(apps[0].Slug).IsEqualTo(PluginSlug);
		await Assert.That(apps[0].NavPlacement).IsEqualTo("Plugins");

		loaded.Loader.Dispose();
	}

	[Test]
	public async Task Catalog_CollectsLoadedFixture_IntoApplicationSourcesBucket()
	{
		var loaded = PluginLoaderService.LoadOne(AppSourceDllPath, NullLogger.Instance)!;
		try
		{
			var catalog = PluginCatalog.ForPlugins([loaded.Plugin]);
			await Assert.That(catalog.ApplicationSources.Count).IsEqualTo(1);
			await Assert.That(catalog.ApplicationSources[0].GetApplications().Single().Slug).IsEqualTo(PluginSlug);
		}
		finally
		{
			loaded.Loader.Dispose();
		}
	}

	[Test]
	public async Task Overlay_UnionsPluginApp_WhenLoaded_AndOmitsIt_WhenCatalogEmpty()
	{
		var inner = new FakeRegistry();
		await inner.UpsertApplicationAsync(DbApp("db-page", order: 10));

		var withoutPlugin = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.Empty(), NullLogger<PluginApplicationRegistryDecorator>.Instance);
		var none = await withoutPlugin.GetApplicationsAsync();
		await Assert.That(none.Select(a => a.Slug)).IsEquivalentTo(new[] { "db-page" });

		var withPlugin = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.ForPlugins([new StubAppPlugin(PluginApp("plugin-page", order: 5))]),
			NullLogger<PluginApplicationRegistryDecorator>.Instance);
		var merged = await withPlugin.GetApplicationsAsync();
		await Assert.That(merged.Select(a => a.Slug)).IsEquivalentTo(new[] { "plugin-page", "db-page" });

		var single = await withPlugin.GetApplicationAsync("plugin-page");
		await Assert.That(single.IsT0).IsTrue();
		await Assert.That(single.AsT0.Slug).IsEqualTo("plugin-page");
	}

	[Test]
	public async Task Overlay_SlugCollision_DbWins_PluginSkipped()
	{
		var inner = new FakeRegistry();
		await inner.UpsertApplicationAsync(DbApp("shared", order: 1, display: "DB Owns This"));

		var decorator = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.ForPlugins([new StubAppPlugin(PluginApp("shared", order: 99, display: "Plugin Loses"))]),
			NullLogger<PluginApplicationRegistryDecorator>.Instance);

		var all = await decorator.GetApplicationsAsync();
		await Assert.That(all.Count).IsEqualTo(1).Because("the colliding plugin overlay is skipped");
		await Assert.That(all[0].DisplayName).IsEqualTo("DB Owns This");

		var single = await decorator.GetApplicationAsync("shared");
		await Assert.That(single.IsT0).IsTrue();
		await Assert.That(single.AsT0.DisplayName).IsEqualTo("DB Owns This");
	}

	[Test]
	public async Task Overlay_Writes_PassThroughForDbSlugs_ButIgnorePluginOwnedSlugs()
	{
		var inner = new FakeRegistry();
		var decorator = new PluginApplicationRegistryDecorator(
			inner, PluginCatalog.ForPlugins([new StubAppPlugin(PluginApp(PluginSlug, order: 5))]),
			NullLogger<PluginApplicationRegistryDecorator>.Instance);

		await decorator.UpsertApplicationAsync(DbApp("editable", order: 2));
		await Assert.That((await decorator.GetApplicationAsync("editable")).IsT0).IsTrue();
		await Assert.That((await inner.GetApplicationAsync("editable")).IsT0).IsTrue();
		await decorator.RemoveApplicationAsync("editable");
		await Assert.That((await inner.GetApplicationAsync("editable")).IsT1).IsTrue();

		await decorator.UpsertApplicationAsync(DbApp(PluginSlug, order: 7, display: "Admin Tried To Edit"));
		await Assert.That((await inner.GetApplicationAsync(PluginSlug)).IsT1).IsTrue()
			.Because("a plugin-owned slug must not be persisted");

		await decorator.RemoveApplicationAsync(PluginSlug);
		var stillThere = await decorator.GetApplicationAsync(PluginSlug);
		await Assert.That(stillThere.IsT0).IsTrue();
		await Assert.That(stillThere.AsT0.DisplayName).IsEqualTo("Plugin Demo");
	}

	private static RegisteredApplication DbApp(string slug, int order, string? display = null) =>
		new(slug, display ?? slug, null, ApplicationKind.Page, $"http/{slug}/schema", null, null,
			PortalRole.Guest, "Build", null, order);

	private static RegisteredApplication PluginApp(string slug, int order, string? display = null) =>
		new(slug, display ?? "Plugin Demo", "Extension", ApplicationKind.Page, $"http/{slug}/schema",
			$"http/{slug}/data", null, PortalRole.Player, "Plugins", null, order);

	private sealed class StubAppPlugin(params RegisteredApplication[] apps) : IPlugin, IApplicationSource
	{
		public string Id => "stub-app";
		public string Version => "1.0.0";
		public IReadOnlyList<string> Dependencies => [];
		public int Priority => 0;
		public void Initialize(IServiceProvider services) { }
		public IEnumerable<RegisteredApplication> GetApplications() => apps;
	}

	/// <summary>An in-memory <see cref="IApplicationRegistryService"/> standing in for the DB-backed inner.</summary>
	private sealed class FakeRegistry : IApplicationRegistryService
	{
		private readonly Dictionary<string, RegisteredApplication> _store =
			new(StringComparer.OrdinalIgnoreCase);

		public Task UpsertApplicationAsync(RegisteredApplication application)
		{
			_store[application.Slug] = application;
			return Task.CompletedTask;
		}

		public Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug) =>
			Task.FromResult(_store.TryGetValue(slug, out var app)
				? (OneOf<RegisteredApplication, NotFound>)app
				: new NotFound());

		public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync() =>
			Task.FromResult<IReadOnlyList<RegisteredApplication>>(_store.Values
				.OrderBy(a => a.Order)
				.ThenBy(a => a.Slug, StringComparer.OrdinalIgnoreCase)
				.ToList());

		public Task RemoveApplicationAsync(string slug)
		{
			_store.Remove(slug);
			return Task.CompletedTask;
		}
	}
}
