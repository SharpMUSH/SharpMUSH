using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

public class PluginSafetyTests
{
	private sealed class Scope : IDisposable
	{
		public PluginDirectories Directories { get; } = new(
			Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")!, Guid.NewGuid().ToString("N"), "built-in"),
			Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")!, Guid.NewGuid().ToString("N"), "installed"));
		public IPluginManager Manager { get; } = Substitute.For<IPluginManager>();
		public IPackageRegistryService Registry { get; } = Substitute.For<IPackageRegistryService>();
		public PackageOperationGate Gate { get; } = new();
		public PluginCatalog Catalog { get; set; } = PluginCatalog.Empty();
		public PluginAdministrationService Service => new(Catalog, Directories, Manager, Registry,
			Gate, new(true),
			NullLogger<PluginAdministrationService>.Instance);

		public void Plugin(string id, params string[] dependencies)
		{
			var directory = Path.Combine(Directories.Installed, id);
			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, "plugin.json"), System.Text.Json.JsonSerializer.Serialize(
				new PluginManifest(id, "1.0", dependencies, 0, null, Entry: "plugin.dll")));
			File.WriteAllText(Path.Combine(directory, "plugin.dll"), "test binary");
		}

		public void Dispose()
		{
			if (Directory.Exists(Path.GetDirectoryName(Directories.Installed))) Directory.Delete(Path.GetDirectoryName(Directories.Installed)!, true);
		}
	}

	[Test]
	public async Task DisablingPluginRefusesConfiguredDependentEvenWithoutPackageDependencyRows()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		scope.Plugin("dependent", "owner");
		var result = await scope.Service.SetEnabledAsync("owner", false);
		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That(PluginState.Read(scope.Directories.StateFile).IsDisabled("owner")).IsFalse();
	}

	[Test]
	public async Task DisablingPluginRefusesContentPackageWithoutUninstallingIt()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		scope.Registry.GetInstalledPackagesAsync().Returns([new InstalledPackageRecord("content", "1", "repo", null, "sha", null,
			DateTimeOffset.UnixEpoch, 1, Plugins: ["owner"])]);
		var result = await scope.Service.SetEnabledAsync("owner", false);
		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That(PluginState.Read(scope.Directories.StateFile).IsDisabled("owner")).IsFalse();
	}

	[Test]
	public async Task DisabledButRunningDependentStillBlocksItsPrerequisite()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		scope.Plugin("dependent", "owner");
		PluginState.Empty.With("dependent", false).Write(scope.Directories.StateFile);
		scope.Manager.Registration("dependent").Returns(new PluginRegistration(false, 0, 0));
		await Assert.That((await scope.Service.GetBlockersAsync("owner", false)).Plugins).Contains("dependent");
	}

	[Test]
	public async Task EnablingRefusesDisabledAndMissingPrerequisites()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		scope.Plugin("dependent", "owner", "missing");
		PluginState.Empty.With("owner", false).With("dependent", false).Write(scope.Directories.StateFile);
		await Assert.That((await scope.Service.GetBlockersAsync("dependent", true)).Plugins).IsEquivalentTo(["owner", "missing"]);
		await Assert.That(await scope.Service.SetEnabledAsync("dependent", true) is Error<string>).IsTrue();
	}

	[Test]
	public async Task DependencyFingerprintChangesWhenCodeOrManifestChangesWithoutVersionChange()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		var before = await scope.Service.ReadDependenciesAsync();
		var path = Path.Combine(scope.Directories.Installed, "owner", "plugin.dll");
		var written = File.GetLastWriteTimeUtc(path);
		File.WriteAllText(path, "evil binary"); // Same length and timestamp: only the code hash distinguishes it.
		File.SetLastWriteTimeUtc(path, written);
		var codeChanged = await scope.Service.ReadDependenciesAsync();
		await Assert.That(codeChanged.Fingerprint).IsNotEqualTo(before.Fingerprint);
		File.WriteAllText(Path.Combine(scope.Directories.Installed, "owner", "plugin.json"), System.Text.Json.JsonSerializer.Serialize(
			new PluginManifest("owner", "1.0", ["hidden"], 0, null, Entry: "plugin.dll")));
		var manifestChanged = await scope.Service.ReadDependenciesAsync();
		await Assert.That(manifestChanged.Fingerprint).IsNotEqualTo(codeChanged.Fingerprint);
		await Assert.That(manifestChanged.Dependencies["owner"]).Contains("hidden");
	}

	[Test]
	public async Task StateChangeRechecksDependenciesAfterWaitingForPackageGate()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var holder = scope.Gate.RunAsync(async () => { held.SetResult(); await release.Task; return 0; });
		await held.Task;
		var change = scope.Service.SetEnabledAsync("owner", false);
		try
		{
			await Assert.That(change.IsCompleted).IsFalse();
			scope.Plugin("arrived-while-waiting", "owner");
		}
		finally { release.SetResult(); await holder; }
		await Assert.That(await change is Error<string>).IsTrue();
		await Assert.That(PluginState.Read(scope.Directories.StateFile).IsDisabled("owner")).IsFalse();
	}

	[Test]
	public async Task RunningPluginRetainsItsDependencyAfterManifestDisappears()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		var dependent = Substitute.For<IPlugin>();
		dependent.Id.Returns("dependent");
		dependent.Dependencies.Returns(["owner"]);
		dependent.Version.Returns("1.0");
		scope.Catalog = PluginCatalog.ForPlugins([dependent]);
		scope.Manager.Registration("dependent").Returns(new PluginRegistration(false, 0, 0));
		await Assert.That((await scope.Service.GetBlockersAsync("owner", false)).Plugins).Contains("dependent");
	}

	[Test]
	public async Task EnablingSchedulesRestartAndLeavesDependentPackagesAbsent()
	{
		using var scope = new Scope();
		scope.Plugin("owner");
		PluginState.Empty.With("owner", false).Write(scope.Directories.StateFile);
		var result = (await scope.Service.SetEnabledAsync("owner", true)).Expect<PluginChangeResponse>();
		await Assert.That(result.Plugin.Pending).IsEqualTo(PluginPendingChange.Starts);
		await Assert.That(result.RemovedPackages).IsEmpty();
		await Assert.That(await scope.Registry.GetInstalledPackagesAsync()).IsEmpty();
	}

	[Test]
	public async Task PluginUnloadedByPackageInstallerIsReportedAsStoppedRatherThanFailed()
	{
		using var scope = new Scope();
		scope.Catalog = PluginCatalog.ForEntries([new PluginBootEntry("owner", null, "1", null,
			PluginOrigin.Installed, Path.Combine(scope.Directories.Installed, "owner"), PluginBootStatus.Loaded, null)]);
		scope.Manager.WasUnloaded("owner").Returns(true);
		var status = (await scope.Service.ListAsync()).Plugins.Single();
		await Assert.That(status.Status).IsEqualTo(PluginBootStatus.Disabled);
		await Assert.That(status.Pending).IsEqualTo(PluginPendingChange.None);
	}

	[Test]
	public async Task UnknownPluginWithDiscoveredDependentsReturnsOrdinaryError()
	{
		using var scope = new Scope();
		scope.Plugin("dependent", "missing");
		var controller = new SharpMUSH.Server.Controllers.PackagePluginsController(scope.Service, null!, new(true), Substitute.For<IAuditLog>());
		var result = await controller.Disable("missing", CancellationToken.None);
		await Assert.That(result.Result is Microsoft.AspNetCore.Mvc.BadRequestObjectResult).IsTrue();
	}
}
