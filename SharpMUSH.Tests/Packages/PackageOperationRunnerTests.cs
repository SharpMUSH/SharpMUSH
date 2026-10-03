using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Portal package operations run as queue entries (#1332), after a backup of the world (#1333). Each
/// runner here has a gate of its own and a stand-in backup service, so nothing it holds or writes
/// reaches another test.
/// </summary>
public class PackageOperationRunnerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private PackageOperationRunner Runner(IWorldBackupService? backups = null) => new(
		Scheduler,
		new PackageOperationGate(),
		backups ?? new RecordingBackups(keep: 0),
		WebAppFactoryArg.Services.GetRequiredService<IPackageLifecycleRunner>(),
		NullLogger<PackageOperationRunner>.Instance);

	[Test]
	public async Task AnOperation_DoesNotRunWhileAQueueEntryIsPartWayThrough()
	{
		var entryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var entryFinished = false;
		var admitted = await Scheduler.AdmitSocketWork(async () =>
		{
			entryStarted.TrySetResult();
			await Task.Delay(400);
			Volatile.Write(ref entryFinished, true);
			return null;
		}, "test-long-entry", "test");
		await Assert.That(admitted.Accepted).IsTrue();
		await entryStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));

		var outcome = await Runner().RunAsync("apply", _ => Task.FromResult(Volatile.Read(ref entryFinished)));

		await Assert.That(outcome.Expect<PackageOperationRan<bool>>().Result).IsTrue()
			.Because("the operation is a queue entry, so it starts only after the running one ends");
	}

	[Test]
	public async Task LifecycleHooks_RunAfterTheOperationsEntry_AndBeforeItAnswers()
	{
		var id = $"runner-hooks-{Guid.NewGuid():N}";
		var manifest = new PackageManifestService().ParseManifest($"""
			package: {id}
			version: "1.0"
			objects:
			  - ref: marker
			    type: thing
			    name: Runner Hook Marker
			    attributes:
			      AINSTALL: |-
			        &INSTALL_MARKER me=installed
			""") switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};
		var request = new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", "commit-1", "main"),
			new Dictionary<string, string>(), []);

		string? markerInsideTheEntry = null;
		var outcome = await Runner().RunAsync("apply", async token =>
		{
			var applied = await Installer.ApplyAsync(manifest, request, token);
			if (applied is PackageApplyResult ok)
			{
				markerInsideTheEntry = await MarkerAsync(ok.CreatedObjects["marker"]);
			}

			return applied;
		}).WaitAsync(TimeSpan.FromSeconds(60));

		var objid = outcome.Expect<PackageOperationRan<Result<PackageApplyResult>>>().Result
			.Expect<PackageApplyResult>().CreatedObjects["marker"];
		await Assert.That(markerInsideTheEntry).IsEqualTo("")
			.Because("AINSTALL must not run inside the operation's queue entry");
		await Assert.That(await MarkerAsync(objid)).IsEqualTo("installed")
			.Because("the hook runs in an entry of its own, which the runner waits for");
		(await Installer.UninstallAsync(id)).Expect<Success>();
	}

	/// <summary>
	/// A second operation that arrives while the first waits its turn is not admitted until the first's
	/// hooks are, so it cannot run between the first operation and the AINSTALL it scheduled.
	/// </summary>
	[Test]
	public async Task ALaterOperation_RunsAfterTheEarlierOnesHooks()
	{
		var id = $"runner-order-{Guid.NewGuid():N}";
		var manifest = new PackageManifestService().ParseManifest($"""
			package: {id}
			version: "1.0"
			objects:
			  - ref: marker
			    type: thing
			    name: Runner Order Marker
			    attributes:
			      AINSTALL: |-
			        &INSTALL_MARKER me=installed
			""") switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};
		var request = new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", "commit-1", "main"),
			new Dictionary<string, string>(), []);

		// Hold the queue so both operations arrive while nothing can run.
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var held = await Scheduler.AdmitSocketWork(async () =>
		{
			await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
			return null;
		}, "test-hold-queue", "test");
		await Assert.That(held.Accepted).IsTrue();

		var runner = Runner();
		string? objid = null;
		var first = runner.RunAsync("apply", async token =>
		{
			var applied = (await Installer.ApplyAsync(manifest, request, token)).Expect<PackageApplyResult>();
			objid = applied.CreatedObjects["marker"];
			return applied;
		});
		var second = runner.RunAsync("apply", async _ => await MarkerAsync(Volatile.Read(ref objid)!));
		release.TrySetResult();

		await first.WaitAsync(TimeSpan.FromSeconds(60));
		var seen = (await second.WaitAsync(TimeSpan.FromSeconds(60))).Expect<PackageOperationRan<string>>().Result;

		await Assert.That(seen).IsEqualTo("installed")
			.Because("the first operation's AINSTALL runs before the second operation does");
		(await Installer.UninstallAsync(id)).Expect<Success>();
	}

	[Test]
	public async Task TheOperationRunsWithoutTheQueueEntrysTimeLimit()
	{
		var outcome = await Runner().RunAsync("apply",
			_ => Task.FromResult(ExecutionBudget.Current?.Remaining ?? TimeSpan.Zero));

		await Assert.That(outcome.Expect<PackageOperationRan<TimeSpan>>().Result).IsEqualTo(TimeSpan.MaxValue);
	}

	[Test]
	public async Task TheWorldIsBackedUpBeforeTheOperation()
	{
		var backups = new RecordingBackups(keep: 2);
		var outcome = await Runner(backups).RunAsync("rollback", _ =>
		{
			backups.Calls.Enqueue("operation");
			return Task.FromResult(1);
		});

		var ran = outcome.Expect<PackageOperationRan<int>>();
		await Assert.That(string.Join(",", backups.Calls)).IsEqualTo("backup,operation");
		await Assert.That(ran.Backup?.Name).IsEqualTo(RecordingBackups.Name);
	}

	[Test]
	public async Task AFailedBackup_RefusesTheOperation()
	{
		var backups = new RecordingBackups(keep: 2, fail: true);
		var ran = false;

		var outcome = await Runner(backups).RunAsync("uninstall", _ =>
		{
			ran = true;
			return Task.FromResult(1);
		});

		await Assert.That(outcome.Expect<PackageOperationRefused>().Reason).Contains("backup");
		await Assert.That(ran).IsFalse();
	}

	[Test]
	public async Task NoBackupIsTaken_WhenTheyAreTurnedOff()
	{
		var backups = new RecordingBackups(keep: 0);

		var outcome = await Runner(backups).RunAsync("apply", _ => Task.FromResult(1));

		await Assert.That(outcome.Expect<PackageOperationRan<int>>().Backup).IsNull();
		await Assert.That(backups.Calls).IsEmpty();
	}

	private async Task<string> MarkerAsync(string objid) =>
		(await Database.GetAttributeAsync(DBRef.Parse(objid), ["INSTALL_MARKER"], CancellationToken.None).LastOrDefaultAsync())
		?.Value.ToPlainText() ?? "";

	/// <summary>A backup service that writes nothing and records when it was asked.</summary>
	private sealed class RecordingBackups(int keep, bool fail = false) : IWorldBackupService
	{
		public const string Name = "20260101-000000-000";

		public System.Collections.Concurrent.ConcurrentQueue<string> Calls { get; } = new();

		public bool IsSupported => true;
		public string UnavailableReason => string.Empty;
		public string Root => "/nonexistent";
		public int Keep => 2;
		public TimeSpan ScheduledInterval => TimeSpan.Zero;
		public int PackageOperationKeep => keep;

		public ValueTask<Result<WorldBackup>> CreateAsync(CancellationToken ct = default) =>
			throw new InvalidOperationException("A package operation must not take a scheduled-retention copy.");

		public IReadOnlyList<WorldBackup> List() => [];

		public ValueTask<Result<WorldBackup>> CreateBeforePackageOperationAsync(CancellationToken ct = default)
		{
			Calls.Enqueue("backup");
			return ValueTask.FromResult<Result<WorldBackup>>(fail
				? new Error<string>("disk full")
				: new WorldBackup(Name, $"/nonexistent/pre-package/{Name}", DateTimeOffset.UtcNow, 0));
		}

		public IReadOnlyList<WorldBackup> ListPackageOperationBackups() => [];
	}
}
