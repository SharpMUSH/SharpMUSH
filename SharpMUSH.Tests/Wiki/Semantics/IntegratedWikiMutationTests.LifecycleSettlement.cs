using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Only the coordinator settles, only after joining a cleanly committed operation, and only the
/// generation its own flow produced. Everything else leaves the package marker UNKNOWN.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static bool Settled(SemanticWikiPrototypeHost host)
		=> LightningStore.IsSettledFixturePackageState(PackageState(host.Services.GetRequiredService<LightningDatabase>()), host.FixtureId);

	private static void ProbePackageRow(ITx tx, string key) => tx.Put(Tables.Pkg, Keys.Str("settlement-probe/" + key), [1]);

	private static Task<PackageOperationOutcome<T>> Coordinated<T>(SemanticWikiPrototypeHost host, Func<CancellationToken, Task<T>> body)
		=> host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("settlement", body);

	[Test]
	public async Task CoordinatedApplySettlesTheGenerationItsOwnFlowProduced()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var outcome = await Coordinated(host, _ => installer.ApplyAsync(MatrixManifest(), MatrixRequest()));
		await Assert.That(outcome.Value is PackageOperationRan<Result<PackageApplyResult>> { Result.Value: PackageApplyResult }).IsTrue();
		await Assert.That(Settled(host)).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Settled(host)).IsTrue();
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task UncertifiedOperationsLeaveTheMarkerUnknown(int mode)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		switch (mode)
		{
			case 0: // A returned error: the transaction reverts, so it reports no clean commit.
				await Coordinated(host, _ => installer.UninstallAsync("settlement-missing"));
				break;
			case 1: // An exception after a committed apply.
				await Assert.That(async () => await Coordinated<int>(host, async _ =>
				{
					await installer.ApplyAsync(MatrixManifest(), MatrixRequest());
					throw new ArithmeticException("after commit");
				})).Throws<ArithmeticException>();
				break;
			default: // Direct installer work, with no coordinator to settle it.
				await installer.ApplyAsync(MatrixManifest(), MatrixRequest());
				break;
		}

		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())[5]).IsEqualTo((byte)0);
		await host.CloseAsync();
	}

	[Test]
	public async Task CapturedWorkAfterReleaseTakesItsOwnAttemptAndInvalidates()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		var release = NewSignal();
		Task? captured = null;
		await Coordinated(host, async _ =>
		{
			var applied = await installer.ApplyAsync(MatrixManifest(), MatrixRequest());
			// Captures this flow, and with it the coordinator's root, then runs after the root is released.
			captured = Task.Run(async () =>
			{
				await release.Task;
				// It keeps the captured root; the entry's own execution budget has ended with the entry.
				using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan);
				using var scope = budget.Enter();
				await using var late = await factory.CreateAsync(new DBRef(7));
				late.Commit();
			});
			return applied;
		});
		await Assert.That(Settled(host)).IsTrue();
		var settledGeneration = PackageGeneration(host);
		release.SetResult();
		await captured!;
		await Assert.That(Settled(host)).IsFalse();
		await Assert.That(PackageGeneration(host)).IsEqualTo(settledGeneration + 1);
		await host.CloseAsync();
	}

	[Test]
	public async Task AChildStillOpenRefusesSettlement()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		PackageWriteTransaction? leaked = null;
		await Coordinated(host, async _ =>
		{
			var applied = await installer.ApplyAsync(MatrixManifest(), MatrixRequest());
			leaked = await factory.CreateAsync(new DBRef(7));
			return applied;
		});
		await Assert.That(Settled(host)).IsFalse();
		leaked!.Commit();
		await leaked.DisposeAsync();
		await Assert.That(Settled(host)).IsFalse();
		await host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AForeignJobInTheSameGroupCommitTaintsTheOperation(bool foreign)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		var root = await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
		try
		{
			var child = (IPackageWriteHold)(await WithAmbient(root, () => provider.BeginSemanticFixturePackageHoldAsync(CancellationToken.None).AsTask()));
			child.Report(PackageWriteVerdict.Committed);
			await child.DisposeAsync();

			provider.Store.PauseWriter();
			Task owned, other = Task.CompletedTask;
			try
			{
				owned = WithAmbient(root, () => provider.Store.WriteAsync(tx => ProbePackageRow(tx, "owned")).AsTask());
				if (foreign) other = provider.Store.WriteAsync(tx => ProbePackageRow(tx, "foreign")).AsTask();
				await Task.Delay(50);
				await Assert.That(owned.IsCompleted).IsFalse();
			}
			finally { provider.Store.ResumeWriter(); }
			await owned;
			await other;

			await Assert.That(await provider.SettleSemanticFixturePackageOperationAsync(root)).IsEqualTo(!foreign);
			await Assert.That(Settled(host)).IsEqualTo(!foreign);
		}
		finally { await root.DisposeAsync(); }
		await host.CloseAsync();
	}

	[Test]
	public async Task AReaderSnapshotKeepsWhatItSawWhileALaterPackageWriteInvalidates()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		await Coordinated(host, _ => installer.ApplyAsync(MatrixManifest(), MatrixRequest()));
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var (before, during) = provider.Store.Read(tx =>
		{
			var first = Marker(tx);
			provider.Store.WriteAsync(write => ProbePackageRow(write, "later")).AsTask().GetAwaiter().GetResult();
			return (first, Marker(tx));
		});
		await Assert.That(LightningStore.IsSettledFixturePackageState(before, host.FixtureId)).IsTrue();
		await Assert.That(LightningStore.IsSettledFixturePackageState(during, host.FixtureId)).IsTrue();
		await Assert.That(provider.Store.Read(tx => LightningStore.IsSettledFixturePackageState(Marker(tx), host.FixtureId))).IsFalse();
		await host.CloseAsync();

		static byte[] Marker(ITx tx) => tx.TryGet(Tables.Meta, PackageSettlementKey, out var value) ? value : [];
	}

	[Test]
	[Arguments(PackageLifecycleOutcome.Absent, QueueOutcome.Completed, true)]
	[Arguments(PackageLifecycleOutcome.Empty, QueueOutcome.Completed, true)]
	[Arguments(PackageLifecycleOutcome.Parsed, QueueOutcome.Completed, false)]
	[Arguments(PackageLifecycleOutcome.Deferred, QueueOutcome.Completed, false)]
	[Arguments(PackageLifecycleOutcome.Absent, QueueOutcome.Failed, false)]
	public async Task OnlyHooksThatRanNoCodeCertify(PackageLifecycleOutcome hook, QueueOutcome queue, bool certifies)
	{
		var observation = new PackageOperationObservation(PackageTaskObservation.Returned, PackageTaskObservation.Returned,
			PackageAdmissionObservation.Accepted, default, true, null, 1, 1, 0, 0, 1, 0,
			new Dictionary<PackageLifecycleOutcome, long> { [hook] = 1 }, new Dictionary<QueueOutcome, long> { [queue] = 1 });
		await Assert.That(SemanticWikiPackageOperationRunner.Certifies(observation)).IsEqualTo(certifies);
		await Assert.That(SemanticWikiPackageOperationRunner.Certifies(observation with { HooksRefused = 1 })).IsFalse();
		await Assert.That(SemanticWikiPackageOperationRunner.Certifies(observation with { Body = PackageTaskObservation.Faulted })).IsFalse();
	}

	private static ulong PackageGeneration(SemanticWikiPrototypeHost host)
		=> System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(PackageState(host.Services.GetRequiredService<LightningDatabase>()).AsSpan(24));

	private static async Task<T> WithAmbient<T>(SemanticFixturePackageOperation root, Func<Task<T>> run)
	{
		using (SemanticFixturePackageOperation.EnterAmbient(root)) return await run();
	}

	private static async Task WithAmbient(SemanticFixturePackageOperation root, Func<Task> run)
	{
		using (SemanticFixturePackageOperation.EnterAmbient(root)) await run();
	}
}
