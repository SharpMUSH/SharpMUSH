using Microsoft.Extensions.Logging.Abstractions;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Proves the <c>examples/plugins/hello-ui</c> worked example end-to-end as a managed-package UI plugin:
/// <list type="bullet">
///   <item>its real DLL — built and copied into <c>plugins-unit/hello-ui/</c> — loads from disk through
///   <see cref="PluginLoaderService"/>, classified <b>load-once</b> (it registers services and contributes
///   UI), and its <see cref="IApplicationSource"/> cast succeeds across the isolation boundary;</item>
///   <item>the contributed <see cref="RegisteredApplication"/> matches what the descriptor declares
///   (slug <c>hello-ui</c>, a Page in the novel "Examples" NavBar section, schema/data routes pointing at
///   its own controller);</item>
///   <item>the app surfaces through the registry overlay (<see cref="PluginApplicationRegistryDecorator"/>)
///   exactly as the booting server would expose it on <c>/api/applications</c>;</item>
///   <item>its <c>examples/packages/hello-ui/package.yaml</c> parses as a valid <c>kind: managed</c> manifest
///   carrying the DLL with a SHA-256 per file.</item>
/// </list>
/// </summary>
[NotInParallel]
public class HelloUiManagedPackageExampleTests
{
	private static string HelloUiDllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "hello-ui", "HelloUiPlugin.dll");

	private const string AppSlug = "hello-ui";
	private const string NavSection = "Examples";

	[Test]
	public async Task ExamplePlugin_LoadsFromDisk_ContributesApplication_AndIsLoadOnce()
	{
		await Assert.That(File.Exists(HelloUiDllPath))
			.IsTrue()
			.Because($"the hello-ui example DLL must be built and copied to {HelloUiDllPath}");

		var loaded = PluginLoaderService.LoadOne(HelloUiDllPath, NullLogger.Instance);
		await Assert.That(loaded).IsNotNull();

		await Assert.That(loaded!.IsUnloadable).IsFalse()
			.Because("a plugin contributing IServiceRegistrar + IApplicationSource is a load-once seam");

		await Assert.That(loaded.Plugin is IServiceRegistrar).IsTrue();
		await Assert.That(loaded.Plugin is IApplicationSource).IsTrue();

		var apps = ((IApplicationSource)loaded.Plugin).GetApplications().ToList();
		await Assert.That(apps.Count).IsEqualTo(1);

		var app = apps[0];
		await Assert.That(app.Slug).IsEqualTo(AppSlug);
		await Assert.That(app.Kind).IsEqualTo(ApplicationKind.Page);
		await Assert.That(app.NavPlacement).IsEqualTo(NavSection);
		await Assert.That(app.SchemaUrl).IsEqualTo("api/hello-ui/schema");
		await Assert.That(app.DataUrl).IsEqualTo("api/hello-ui/data");

		loaded.Loader.Dispose();
	}

	[Test]
	public async Task ExamplePlugin_App_SurfacesThroughRegistryOverlay()
	{
		var loaded = PluginLoaderService.LoadOne(HelloUiDllPath, NullLogger.Instance)!;
		try
		{
			var catalog = PluginCatalog.ForPlugins([loaded.Plugin]);
			var decorator = new PluginApplicationRegistryDecorator(
				new EmptyRegistry(), catalog, NullLogger<PluginApplicationRegistryDecorator>.Instance);

			var all = await decorator.GetApplicationsAsync();
			await Assert.That(all.Select(a => a.Slug)).Contains(AppSlug);

			var single = await decorator.GetApplicationAsync(AppSlug);
			await Assert.That(single.IsT0).IsTrue();
			await Assert.That(single.AsT0.DisplayName).IsEqualTo("Hello UI");
			await Assert.That(single.AsT0.NavPlacement).IsEqualTo(NavSection);
		}
		finally
		{
			loaded.Loader.Dispose();
		}
	}

	[Test]
	public async Task ExamplePackageManifest_IsAValidManagedPackage()
	{
		var manifestPath = ManifestPath();
		var result = new PackageManifestService().ParseManifest(await File.ReadAllTextAsync(manifestPath));

		await Assert.That(result.IsT0).IsTrue()
			.Because($"{manifestPath} must parse as a valid manifest");

		var manifest = result.AsT0.Manifest;
		await Assert.That(manifest.Name).IsEqualTo(AppSlug);
		await Assert.That(manifest.Binary).IsNotNull()
			.Because("a kind: managed package must carry a binaries block");
		await Assert.That(manifest.Binary!.Files.Any(f =>
			f.FileName.Equals("HelloUiPlugin.dll", StringComparison.OrdinalIgnoreCase))).IsTrue();
		await Assert.That(manifest.Binary.Files.All(f => f.Sha256.Length == 64)).IsTrue();
	}

	private static string ManifestPath()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, "examples", "packages", "hello-ui", "package.yaml");
			if (File.Exists(candidate))
			{
				return candidate;
			}

			dir = dir.Parent!;
		}

		throw new FileNotFoundException("Could not locate examples/packages/hello-ui/package.yaml above the test directory.");
	}

	/// <summary>An empty <see cref="IApplicationRegistryService"/> inner — proves the plugin overlay alone surfaces the app.</summary>
	private sealed class EmptyRegistry : IApplicationRegistryService
	{
		public Task UpsertApplicationAsync(RegisteredApplication application) => Task.CompletedTask;

		public Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug) =>
			Task.FromResult<OneOf<RegisteredApplication, NotFound>>(new NotFound());

		public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync() =>
			Task.FromResult<IReadOnlyList<RegisteredApplication>>([]);

		public Task RemoveApplicationAsync(string slug) => Task.CompletedTask;
	}
}
