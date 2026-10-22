using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Commands.Database;
using Mediator;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;

using SharpMUSH.Tests.Packages;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task FixturePackageBeginCommitsBeforeSourceFailureOrProvenance(int mode)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		using var cancellation = new CancellationTokenSource();
		if (mode == 3)
		{
			var backups = host.Services.GetRequiredService<IWorldBackupService>();
			await Assert.That(backups.IsSupported && backups.PackageOperationKeep > 0).IsTrue();
		}
		var beganBeforeSource = false;
		var bodyEntered = false;
		async Task Mutate()
		{
			// Independent native read: source must see a prior COMMITTED transaction.
			beganBeforeSource = PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1));
			(await wiki.CreateAsync("Begin witness", "source survived failure", "#1", sourceLocale: "en", slug: "begin-witness")).Expect<WikiPage>();
		}
		async Task<bool> Body(CancellationToken ct)
		{
			bodyEntered = true;
			await Mutate();
			if (mode == 1) throw new ArithmeticException("Owned source failure");
			if (mode == 2) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
			return false; // A returned business failure must not clear UNKNOWN either.
		}
		var runner = host.Services.GetRequiredService<IPackageOperationRunner>();
		PackageOperationExecution<bool>? owned = null;
		Task<PackageOperationOutcome<bool>> task;
		if (mode == 2)
		{
			if (runner is not IPackageOperationLifetime lifetime) throw new InvalidOperationException("Expected retained coordinator capability");
			owned = await lifetime.StartAsync("begin-failure", Body, cancellation.Token);
			task = owned.Completion.WaitAsync(cancellation.Token);
		}
		else task = runner.RunAsync("begin-failure", Body, cancellation.Token, provenance: mode == 3 ? async _ =>
		{
			await Mutate();
			throw new ArithmeticException("Owned provenance failure");
		}
			: null);
		try
		{
			if (mode == 1) await Assert.That(() => task).Throws<ArithmeticException>();
			else if (mode == 3) await Assert.That(await task is PackageOperationRefused).IsTrue();
			else if (mode == 2) await Assert.That(() => task).Throws<OperationCanceledException>();
			else await Assert.That(await task is PackageOperationRan<bool> { Result: false }).IsTrue();
		}
		finally
		{
			await host.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests(TimeSpan.FromSeconds(30));
			if (owned is not null)
			{
				try { await owned.Completion; }
				catch (OperationCanceledException) when (cancellation.IsCancellationRequested && owned.Completion.IsCanceled) { }
				var report = await owned.Observation;
				await Assert.That(report.Coordinator).IsEqualTo(PackageTaskObservation.Cancelled);
				await Assert.That(report.Body).IsEqualTo(PackageTaskObservation.Cancelled);
			}
		}
		await Assert.That(beganBeforeSource).IsTrue();
		await Assert.That(bodyEntered).IsEqualTo(mode != 3);
		await Assert.That(await wiki.GetBySlugAsync("begin-witness") is WikiPage).IsTrue();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>()).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task FixturePackageBeginCoversDirectInstallerBeforeItsGate(int entry)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var release = NewSignal();
		var started = NewSignal();
		var held = host.Services.GetRequiredService<IPackageOperationGate>().RunAsync(async () =>
		{ started.TrySetResult(); await release.Task; return 0; });
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		Task? task = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			task = entry switch
			{
				0 => installer.ApplyAsync(MatrixManifest(), MatrixRequest()),
				1 => installer.UninstallAsync("begin-missing"),
				_ => installer.RollbackAsync("begin-missing", 1)
			};
			// A later writer job is a deterministic barrier after BEGIN submission, not a sleep.
			await provider.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("begin-barrier"), [1]));
			await Assert.That(task.IsCompleted).IsFalse();
			await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		}
		finally { release.TrySetResult(); await held; if (task is not null) await task; }
		await Assert.That(PackageState(provider)[5]).IsEqualTo((byte)0);
	}

	[Test]
	public async Task FixturePackageBeginOverflowPreventsSourceEntry()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.CloseAsync();
		// Corrupt only this fully closed, exclusively owned disposable world's counter.
		using (var setup = new LightningStore(new LightningStoreOptions { Path = host.WorldPath }))
			await setup.WriteAsync(tx => tx.Put(Tables.Meta, PackageSettlementKey, UnknownPackageState(host.FixtureId, ulong.MaxValue)));
		await host.OpenAsync();
		var entered = false;
		await Assert.That(async () => await host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("begin-overflow", _ =>
		{ entered = true; return Task.FromResult(1); })).Throws<OverflowException>();
		await Assert.That(entered).IsFalse();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>()).SequenceEqual(UnknownPackageState(host.FixtureId, ulong.MaxValue))).IsTrue();
	}

	[Test]
	public async Task FixturePackageBeginCancelledBehindWriterNeverEntersSource()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var started = NewSignal();
		using var release = new ManualResetEventSlim(false);
		var blocker = Task.Run(async () => await provider.Store.WriteAsync(_ =>
		{
			started.TrySetResult();
			if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Owned BEGIN barrier");
		}));
		using var cancellation = new CancellationTokenSource();
		Task? task = null;
		var entered = false;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			task = host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("begin-cancelled", _ =>
			{ entered = true; return Task.FromResult(1); }, cancellation.Token);
			cancellation.Cancel();
		}
		finally { release.Set(); await blocker; }
		await Assert.That(() => task!).Throws<OperationCanceledException>();
		await host.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests(TimeSpan.FromSeconds(30));
		await Assert.That(entered).IsFalse();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		// A coordinated BEGIN that never entered left the history untouched, so activation is still admitted.
		await host.ActivateAtomicWikiProjectionAsync();
		await Assert.That(Settled(host)).IsTrue();
	}

	[Test]
	public async Task FixturePackageBeginRetainsActualCoordinatorLifetime()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var runner = host.Services.GetRequiredService<IPackageOperationRunner>();
		await Assert.That(runner is IPackageOperationLifetime).IsTrue();
		if (runner is not IPackageOperationLifetime lifetime) return;
		var started = NewSignal();
		var release = NewSignal();
		var execution = await lifetime.StartAsync("begin-owned", async _ =>
		{ started.TrySetResult(); await release.Task; return false; });
		Task<PackageOperationExecution<int>>? next = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			using var waiter = new CancellationTokenSource();
			waiter.Cancel();
			await Assert.That(() => execution.Completion.WaitAsync(waiter.Token)).Throws<OperationCanceledException>();
			await Assert.That(execution.Completion.IsCompleted).IsFalse();
			// A second coordinator would not share this sequence, even though the queue is occupied.
			next = host.Services.GetRequiredService<PackageOperationRunner>().StartAsync("same-coordinator", _ => Task.FromResult(1));
			await Assert.That(next.IsCompleted).IsFalse();
		}
		finally
		{
			release.TrySetResult();
			await execution.Completion;
			await execution.Observation;
			if (next is not null)
			{
				var second = await next;
				await second.Completion;
				await second.Observation;
			}
		}
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>()).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
	}
	[Test]
	public async Task FixturePackageBeginNestedInstallerNeverSettlesOuterAttempt()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var beforeInner = false;
		var afterInner = false;
		await host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("begin-nested", async _ =>
		{
			beforeInner = PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1));
			await host.Services.GetRequiredService<IPackageInstallService>().UninstallAsync("begin-nested-missing");
			afterInner = PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 2));
			return false;
		});
		await Assert.That(beforeInner && afterInner).IsTrue();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 2))).IsTrue();
	}

	[Test]
	public async Task FixturePackageBeginRejectsStaleProviderAfterReopenBeforeSource()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var stale = host.Services.GetRequiredService<IPackageOperationRunner>();
		await host.CloseAsync();
		await host.OpenAsync();
		var entered = false;
		await Assert.That(async () => await stale.RunAsync("stale-begin", _ =>
		{ entered = true; return Task.FromResult(1); })).Throws<WikiFixtureMutationException>();
		await Assert.That(entered).IsFalse();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>()).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
	}

	[Test]
	public async Task FixturePackageBeginRejectsForeignOwnerAndForgedLeaseWithoutWriting()
	{
		await using var host = NewHost();
		await using var other = NewHost();
		await host.OpenAsync();
		await other.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		var foreign = ((SemanticWikiPackageOperationRunner)other.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		await Assert.That(async () => await provider.BeginSemanticFixturePackageAttemptAsync(foreign, CancellationToken.None))
			.Throws<WikiFixtureMutationException>();
		var forged = new SemanticFixtureLease(owner, false);
		forged.MarkInitialized();
		await Assert.That(async () => await provider.Store.BeginFixturePackageAttemptAsync(forged, CancellationToken.None))
			.Throws<WikiFixtureMutationException>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		await Assert.That(PackageState(other.Services.GetRequiredService<LightningDatabase>()).SequenceEqual(UnknownPackageState(other.FixtureId, 0))).IsTrue();
	}

	[Test]
	public async Task FixturePackageBeginSurvivesActualAttributeCompensation()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var services = host.Services;
		var provider = services.GetRequiredService<LightningDatabase>();
		var god = (await services.GetRequiredService<IObjectStore>().GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var mediator = services.GetRequiredService<IMediator>();
		await mediator.Send(new SetAttributeCommand(new DBRef(1), ["BEGIN_UNDO"], MarkupText.Plain("before"), god));
		var beganBeforeWrite = false;
		var forwardWritten = false;
		await Assert.That(async () => await services.GetRequiredService<IPackageOperationRunner>().RunAsync<int>("begin-compensation", async ct =>
		{
			beganBeforeWrite = PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1));
			await using var writes = TestPackageWrites.Issue(services, god);
			await writes.SetAttributeAsync(new DBRef(1), ["BEGIN_UNDO"], MarkupText.Plain("changed"), god, ct);
			forwardWritten = (await provider.GetAttributeAsync(new DBRef(1), ["BEGIN_UNDO"], ct).LastAsync()).Value.ToPlainText() == "changed";
			throw new ArithmeticException("Owned compensation failure");
		})).Throws<ArithmeticException>();
		await Assert.That(beganBeforeWrite && forwardWritten).IsTrue();
		await Assert.That((await provider.GetAttributeAsync(new DBRef(1), ["BEGIN_UNDO"], CancellationToken.None).LastAsync()).Value.ToPlainText()).IsEqualTo("before");
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
	}

}
