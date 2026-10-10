using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// What the loader makes of a plugins folder before it loads anything: which DLL is the plugin, which plugins it
/// refuses and why, and the turned-off list it reads. Built over copies of the CommandOnlyPlugin fixture in scratch
/// folders; none of these load an assembly.
/// </summary>
public class PluginDiscoveryTests
{
	private static string Folder(string root, string id, string? manifest, params string[] dlls)
	{
		var folder = Path.Combine(root, id);
		Directory.CreateDirectory(folder);
		foreach (var dll in dlls)
		{
			File.Copy(PluginPackageFixture.DllPath, Path.Combine(folder, dll));
		}

		if (manifest is not null)
		{
			File.WriteAllText(Path.Combine(folder, PluginManifest.FileName), manifest);
		}

		return folder;
	}

	private static async Task InScratch(Func<PluginDirectories, Task> test)
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			await test(directories);
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	private static PluginLoaderService.PluginCandidate DiscoverOne(string root) =>
		PluginLoaderService.Discover(root, NullLogger.Instance).Single();

	[Test]
	public Task TwoDllsWithoutEntry_IsNotLoaded() => InScratch(async directories =>
	{
		Folder(directories.Installed, "two", """{ "id": "two" }""", "A.dll", "B.dll");

		var candidate = DiscoverOne(directories.Installed);
		await Assert.That(candidate.Problem).IsEqualTo(PluginBootStatus.Failed)
			.Because("the first DLL by name is not a reliable guess at the plugin");
		await Assert.That(candidate.Reason!).Contains("entry");
	});

	[Test]
	public Task Entry_PicksTheNamedDll() => InScratch(async directories =>
	{
		Folder(directories.Installed, "named", """{ "id": "named", "entry": "B.dll", "name": "Named Plugin" }""", "A.dll", "B.dll");

		var candidate = DiscoverOne(directories.Installed);
		await Assert.That(candidate.Problem).IsNull();
		await Assert.That(Path.GetFileName(candidate.DllPath)).IsEqualTo("B.dll");
		await Assert.That(candidate.Name).IsEqualTo("Named Plugin");
	});

	[Test]
	public Task EntryMissing_IsNotLoaded() => InScratch(async directories =>
	{
		Folder(directories.Installed, "missing", """{ "id": "missing", "entry": "Gone.dll" }""", "A.dll");

		var candidate = DiscoverOne(directories.Installed);
		await Assert.That(candidate.Problem).IsEqualTo(PluginBootStatus.Failed);
		await Assert.That(candidate.Reason!).Contains("Gone.dll");
	});

	[Test]
	public Task NewerContract_IsIncompatible() => InScratch(async directories =>
	{
		Folder(directories.Installed, "future", """{ "id": "future", "minServerVersion": ">=99.0" }""", "A.dll");

		var candidate = DiscoverOne(directories.Installed);
		await Assert.That(candidate.Problem).IsEqualTo(PluginBootStatus.Incompatible);
		await Assert.That(candidate.Reason!).Contains(PluginContractVersion.Current.ToString());
	});

	[Test]
	public Task OlderContractMajor_IsIncompatible() => InScratch(async directories =>
	{
		Folder(directories.Installed, "past", """{ "id": "past", "minServerVersion": ">=1.0" }""", "A.dll");

		var candidate = DiscoverOne(directories.Installed);
		await Assert.That(candidate.Problem).IsEqualTo(PluginBootStatus.Incompatible)
			.Because("a plugin built against 1.x does not bind to the 2.x contract");
	});

	[Test]
	public Task DotFolders_AreNotPlugins() => InScratch(async directories =>
	{
		Folder(directories.Installed, ".uploads", null, "A.dll");
		Folder(directories.Installed, ".incoming-x-1", """{ "id": "x" }""", "A.dll");

		await Assert.That(PluginLoaderService.Discover(directories.Installed, NullLogger.Instance)).IsEmpty();
	});

	[Test]
	public Task InstalledCopyOfAShippedPlugin_IsADuplicate_AndTurnedOffLoadsNothing() => InScratch(async directories =>
	{
		Folder(directories.BuiltIn, "same", """{ "id": "same" }""", "A.dll");
		Folder(directories.Installed, "same", """{ "id": "same" }""", "A.dll");

		var report = PluginLoaderService.LoadAll(directories, PluginState.Empty.With("same", enabled: false), NullLogger.Instance);

		await Assert.That(report.Loaded).IsEmpty().Because("a turned-off plugin is found and reported, not loaded");
		var builtIn = report.Entries.Single(e => e.Origin == PluginOrigin.BuiltIn);
		var installed = report.Entries.Single(e => e.Origin == PluginOrigin.Installed);
		await Assert.That(builtIn.Status).IsEqualTo(PluginBootStatus.Disabled);
		await Assert.That(installed.Status).IsEqualTo(PluginBootStatus.Duplicate)
			.Because("the shipped plugin is found first, so an installed one cannot stand in for it");
	});

	[Test]
	public Task State_RoundTrips_AndMissingIsEmpty() => InScratch(async directories =>
	{
		await Assert.That(PluginState.Read(directories.StateFile).Disabled).IsEmpty();

		PluginState.Empty.With("scene", enabled: false).With("other", enabled: false).With("other", enabled: true)
			.Write(directories.StateFile);

		var read = PluginState.Read(directories.StateFile);
		await Assert.That(read.Disabled).IsEquivalentTo(["scene"]);
		await Assert.That(read.IsDisabled("SCENE")).IsTrue();
		await Assert.That(File.Exists(directories.StateFile + ".tmp")).IsFalse();
	});

	[Test]
	public Task State_Unreadable_Throws() => InScratch(async directories =>
	{
		Directory.CreateDirectory(directories.Installed);
		await File.WriteAllTextAsync(directories.StateFile, "{ not json");

		await Assert.That(() => PluginState.Read(directories.StateFile)).Throws<JsonException>()
			.Because("starting every plugin an administrator turned off is worse than refusing to start");
	});
}
