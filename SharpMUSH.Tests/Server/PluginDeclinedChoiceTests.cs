using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

public sealed class PluginDeclineWorld : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

public class PluginDeclinedChoiceTests
{
	[ClassDataSource<PluginDeclineWorld>(Shared = SharedType.PerClass)]
	public required PluginDeclineWorld World { get; init; }

	[Test]
	public async Task EnablingPluginPreservesPersistedBundledPackageDeclines()
	{
		var services = World.Services;
		var features = services.GetRequiredService<GameFeatureService>();
		var data = services.GetRequiredService<IExpandedObjectDataService>();
		await features.SetDeclinedAsync("scene", true);
		var before = (await data.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds.ToArray();
		await Assert.That(before).Contains("scene");

		var root = Path.Combine(PluginTestScratch.Root(Environment.GetEnvironmentVariable("TMPDIR")), "plugin-declines-" + Guid.NewGuid().ToString("N"));
		var directories = new PluginDirectories(Path.Combine(root, "built-in"), Path.Combine(root, "installed"));
		try
		{
			var directory = Path.Combine(directories.BuiltIn, "scene");
			Directory.CreateDirectory(directory);
			await File.WriteAllTextAsync(Path.Combine(directory, "plugin.dll"), "test binary");
			await File.WriteAllTextAsync(Path.Combine(directory, "plugin.json"), """{"id":"scene","version":"1","dependencies":[],"entry":"plugin.dll"}""");
			PluginState.Empty.With("scene", false).Write(directories.StateFile);
			var plugins = ActivatorUtilities.CreateInstance<PluginAdministrationService>(services, directories);

			(await plugins.SetEnabledAsync("scene", true)).Expect<PluginChangeResponse>();

			await Assert.That((await data.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds)
				.IsEquivalentTo(before);
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}
}
