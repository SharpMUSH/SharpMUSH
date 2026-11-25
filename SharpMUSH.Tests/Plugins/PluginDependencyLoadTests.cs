using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using PM = SharpMUSH.Implementation.Services.PluginManager;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// A plugin that declares another as a dependency resolves that plugin's assemblies from its load context (#2880):
/// the fixtures <c>dependency-base</c> and <c>dependent-sample</c> (which does not carry the base's DLL) are loaded
/// from disk together, as a booting server loads them.
/// </summary>
public class PluginDependencyLoadTests
{
	private const string BaseId = "dependency-base";
	private const string DependentId = "dependent-sample";

	private static readonly IServiceProvider EmptyProvider = new EmptyServiceProvider();

	private static PluginDirectories Directories(params string[] folders)
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		foreach (var folder in folders)
		{
			var source = Path.Combine(AppContext.BaseDirectory, "plugins-unit", folder);
			var target = Path.Combine(directories.Installed, folder);
			Directory.CreateDirectory(target);
			foreach (var file in Directory.EnumerateFiles(source))
			{
				File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
			}
		}

		return directories;
	}

	private static Assembly AssemblyOf(PluginLoaderService.LoadReport report, string id) =>
		report.Loaded.Single(l => l.Plugin.Id == id).Plugin.GetType().Assembly;

	private static PM NewManager() =>
		new(PluginCatalog.Empty(), [], [], EmptyProvider, NullLogger<PM>.Instance);

	private static async Task InScratch(string[] folders, Func<PluginLoaderService.LoadReport, Task> test)
	{
		var directories = Directories(folders);
		try
		{
			var report = PluginLoaderService.LoadAll(directories, PluginState.Empty, NullLogger.Instance);
			try
			{
				await test(report);
			}
			finally
			{
				foreach (var loaded in report.Loaded)
				{
					loaded.Loader.Dispose();
				}
			}
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public Task DependentSharesTheDependencysTypes() => InScratch([BaseId, DependentId], async report =>
	{
		await Assert.That(report.Entries.Select(e => (e.Id, e.Status)))
			.IsEquivalentTo([(BaseId, PluginBootStatus.Loaded), (DependentId, PluginBootStatus.Loaded)]);
		await Assert.That(report.Loaded.Select(l => l.Plugin.Id)).IsEquivalentTo([BaseId, DependentId]);

		var baseAssembly = AssemblyOf(report, BaseId);
		var behavior = baseAssembly.GetType("DependencyBasePlugin.IGreetingBehavior")!;
		var loud = AssemblyOf(report, DependentId).GetType("DependentPlugin.LoudGreeting")!;
		await Assert.That(behavior.IsAssignableFrom(loud)).IsTrue()
			.Because("the dependent's behavior implements the dependency's own interface, not a second copy");
	});

	[Test]
	public Task BehaviorTheDependentAddsReachesTheDependency() => InScratch([BaseId, DependentId], async report =>
	{
		var manager = NewManager();
		foreach (var loaded in report.Loaded)
		{
			manager.RegisterPlugin(loaded.Plugin);
		}

		var registry = AssemblyOf(report, BaseId).GetType("DependencyBasePlugin.GreetingBehaviors")!;
		var all = (System.Collections.IEnumerable)registry.GetProperty("All")!.GetValue(null)!;
		var greetings = all.Cast<object>()
			.Select(b => (string)b.GetType().GetMethod("Greet")!.Invoke(b, ["you"])!)
			.ToList();
		await Assert.That(greetings).IsEquivalentTo(["HELLO YOU"]);
	});

	[Test]
	public Task DependencyDoesNotUnloadUnderItsDependent() => InScratch([BaseId, DependentId], async report =>
	{
		var manager = NewManager();
		foreach (var loaded in report.Loaded)
		{
			manager.RegisterPlugin(loaded.Plugin);
		}

		var refused = (await manager.UnloadAsync(BaseId)).Expect<Error<string>>("a loaded dependent holds its types");
		await Assert.That(refused.Value).Contains($"'{DependentId}'");
		await Assert.That(manager.Registration(BaseId)).IsNotNull();
		await Assert.That((await manager.ReloadAsync(BaseId)) is Error<string>).IsTrue();

		_ = (await manager.UnloadAsync(DependentId)).Expect<Success>("nothing depends on the dependent");
		_ = (await manager.UnloadAsync(BaseId)).Expect<Success>("its dependent is gone");
	});

	[Test]
	public Task ReloadedDependentStillSharesTheDependency() => InScratch([BaseId, DependentId], async report =>
	{
		var manager = NewManager();
		foreach (var loaded in report.Loaded)
		{
			manager.RegisterPlugin(loaded.Plugin);
		}

		_ = (await manager.ReloadAsync(DependentId)).Expect<Success>("it loads again with the same dependency");
		await Assert.That(manager.Registration(DependentId)).IsNotNull();

		var registry = AssemblyOf(report, BaseId).GetType("DependencyBasePlugin.GreetingBehaviors")!;
		var all = (System.Collections.IEnumerable)registry.GetProperty("All")!.GetValue(null)!;
		await Assert.That(all.Cast<object>().Count()).IsEqualTo(2)
			.Because("the reloaded dependent's Initialize added its behavior to the same dependency");
	});

	[Test]
	public Task DependentWithoutItsDependencyFails() => InScratch([DependentId], async report =>
	{
		await Assert.That(report.Loaded).IsEmpty();
		var entry = report.Entries.Single();
		await Assert.That(entry.Status).IsEqualTo(PluginBootStatus.Failed);
		await Assert.That(entry.Reason).Contains($"depends on '{BaseId}', which is not running");
	});

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}
}
