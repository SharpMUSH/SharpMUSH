using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

using SharpMUSH.Tests.Packages;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task FixtureRetainedOperationRefusesCloseWithoutStoppingSourceOrUndo(int entry)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var services = host.Services;
		var provider = services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)services.GetRequiredService<IPackageOperationRunner>()).Owner;
		var mediator = services.GetRequiredService<IMediator>();
		var god = (await provider.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		await mediator.Send(new SetAttributeCommand(new DBRef(1), ["RETENTION_VALUE"], MarkupText.Plain("before"), god));
		var operation = await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
		try
		{
			await Assert.That(async () =>
			{
				switch (entry)
				{
					case 0: await host.CloseAsync(); break;
					case 1: await host.DisposeAsync(); break;
					case 2: await provider.DisposeAsync(); break;
					case 3: provider.Store.Dispose(); break;
					case 4: operation.Lease.Dispose(); break;
				}
			}).Throws<InvalidOperationException>();
			await Assert.That(ReferenceEquals(services, host.Services)).IsTrue();
			await Assert.That(ReferenceEquals(provider, host.Services.GetRequiredService<LightningDatabase>())).IsTrue();
			await Assert.That(owner.IsQuarantined).IsFalse();
			await Assert.That(() => owner.Dispose()).Throws<InvalidOperationException>();
			await using (var writes = TestPackageWrites.Issue(services, god))
			{
				await writes.SetAttributeAsync(new DBRef(1), ["RETENTION_VALUE"], MarkupText.Plain("changed"), god, CancellationToken.None);
				await Assert.That((await provider.GetAttributeAsync(new DBRef(1), ["RETENTION_VALUE"]).LastAsync()).Value.ToPlainText()).IsEqualTo("changed");
			}
			await Assert.That((await provider.GetAttributeAsync(new DBRef(1), ["RETENTION_VALUE"]).LastAsync()).Value.ToPlainText()).IsEqualTo("before");
			await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		}
		finally { await operation.DisposeAsync(); }
		await operation.DisposeAsync();
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(ReferenceEquals(provider, reopened)).IsFalse();
		await Assert.That((await reopened.GetAttributeAsync(new DBRef(1), ["RETENTION_VALUE"]).LastAsync()).Value.ToPlainText()).IsEqualTo("before");
		await using var current = await reopened.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
		await operation.DisposeAsync();
		await Assert.That(async () => await host.CloseAsync()).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task FixtureRetainedOperationCancelledBehindWriterJoinsBeforeReleasingHold()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		var started = NewSignal();
		using var release = new ManualResetEventSlim(false);
		var blocker = Task.Run(async () => await provider.Store.WriteAsync(_ =>
		{
			started.TrySetResult();
			if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Retained native BEGIN barrier");
		}));
		using var cancellation = new CancellationTokenSource();
		Task<SemanticFixturePackageOperation>? pending = null;
		Task? close = null;
		Exception? failure = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			pending = provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, cancellation.Token).AsTask();
			cancellation.Cancel();
			await Assert.That(pending.IsCompleted).IsFalse();
			// Disposal is synchronous at its native boundary; isolate it so a missing busy guard
			// cannot strand this test behind the writer barrier it owns.
			close = Task.Run(async () => await provider.DisposeAsync());
			await Assert.That(async () => await close.WaitAsync(TimeSpan.FromSeconds(10))).Throws<InvalidOperationException>();
			await Assert.That(owner.IsQuarantined).IsFalse();
		}
		catch (Exception error) { failure = error; }
		finally
		{
			release.Set();
			var cleanupFailures = new List<Exception>();
			try { await blocker; } catch (Exception error) { cleanupFailures.Add(error); }
			if (pending is not null)
			{
				try { await using var admitted = await pending; }
				catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
				catch (Exception error) { cleanupFailures.Add(error); }
			}
			if (close is not null)
			{
				try { await close; }
				catch (SemanticFixtureOperationInProgressException) { }
				catch (Exception error) { cleanupFailures.Add(error); }
			}
			if (cleanupFailures.Count != 0)
			{
				if (failure is not null) cleanupFailures.Insert(0, failure);
				throw new AggregateException("Retained BEGIN test and owned cleanup failed.", cleanupFailures);
			}
		}
		if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
		if (pending is not { } attempt) throw new InvalidOperationException("BEGIN was not attempted");
		await Assert.That(async () => await attempt).Throws<OperationCanceledException>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	public async Task FixtureRetainedOperationNativeBeginFailureDoesNotLeakHold()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.CloseAsync();
		using (var setup = new LightningStore(new LightningStoreOptions { Path = host.WorldPath }))
			await setup.WriteAsync(tx => tx.Put(Tables.Meta, PackageSettlementKey, UnknownPackageState(host.FixtureId, ulong.MaxValue)));
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		await Assert.That(async () => await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None)).Throws<OverflowException>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, ulong.MaxValue))).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
	}
	[Test]
	public async Task FixtureRetainedOperationClosingExcludesPackagesButPreservesShutdownWrites()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		provider.PrepareSemanticFixtureClose();
		await AssertFixtureFailure(() => provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None).AsTask(), WikiFixtureMutationFailure.Unavailable);
		await AssertFixtureFailure(() => provider.Store.WriteAsync(tx => tx.Put(Tables.PkgRemote, [101], [1])).AsTask(), WikiFixtureMutationFailure.Unavailable);
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		// A closing host still has ordinary-table activity/configuration flushes to persist.
		await provider.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("retained-shutdown"), [42]));
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(reopened.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("retained-shutdown"), out var value) && value.SequenceEqual(new byte[] { 42 }))).IsTrue();
		await using var fresh = await reopened.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
	}

	[Test]
	public async Task FixtureRetainedOperationRequiresEveryActualHoldToRelease()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		await using var first = await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
		await using var second = await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
		await first.DisposeAsync();
		await first.DisposeAsync();
		await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 2))).IsTrue();
		var forged = new SemanticFixtureLease(owner, false);
		forged.MarkInitialized();
		await Assert.That(() => owner.RetainPackageOperation(forged)).Throws<WikiFixtureMutationException>();
		await Assert.That(() => owner.ReleasePackageOperation(new SemanticFixturePackageOperation(forged))).Throws<InvalidOperationException>();
		forged.Dispose();
		await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
		await second.DisposeAsync();
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await using var current = await reopened.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
		await first.DisposeAsync();
		await second.DisposeAsync();
		await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
	}

	[Test]
	public async Task FixtureRetainedOperationLeaseCannotReleaseBeforeConfirmedNativeClose()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		var operation = await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None);
		await operation.DisposeAsync();
		await Assert.That(() => operation.Lease.Dispose()).Throws<InvalidOperationException>();
		await Assert.That(owner.IsQuarantined).IsFalse();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		await host.CloseAsync();
		operation.Lease.Dispose();
		await host.OpenAsync();
		await AssertFixtureFailure(() => provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None).AsTask(), WikiFixtureMutationFailure.Unavailable);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FixtureRetainedOperationRejectsUnissuedRelease(bool foreignOwner)
	{
		var scratch = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")
			?? Path.Combine(Environment.CurrentDirectory, "work/semantic-wiki-prototype/b1/tmp"), "retention-ownership");
		await using var fixture = new SemanticWikiPrototypeFixture(scratch);
		await using var foreign = new SemanticWikiPrototypeFixture(scratch);
		var owner = fixture.Capability;
		using var lease = owner.Claim(new LightningStoreOptions { Path = fixture.WorldPath });
		lease.Initialized();
		var actual = owner.RetainPackageOperation(lease);
		Exception? refusal = null;
		Exception? actualReleaseFailure = null;
		try
		{
			if (foreignOwner) foreign.Capability.ReleasePackageOperation(actual);
			else await new SemanticFixturePackageOperation(lease).DisposeAsync();
		}
		catch (Exception error) { refusal = error; }
		// There is no native environment in this regression. Always release managed ownership,
		// even on RED when an unissued release has consumed the real operation's count.
		try { await actual.DisposeAsync(); }
		catch (Exception error) { actualReleaseFailure = error; }
		await Assert.That(refusal is InvalidOperationException).IsTrue();
		await Assert.That(actualReleaseFailure is null).IsTrue();
	}

	[Test]
	[Arguments(0, false)]
	[Arguments(1, false)]
	[Arguments(2, false)]
	[Arguments(0, true)]
	[Arguments(1, true)]
	[Arguments(2, true)]
	public async Task FixtureWriterCallbackRefusesCloseBeforeChangingIssuance(int entry, bool queued)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		Exception? refusal = null;
		void Write(ITx tx)
		{
			try
			{
				switch (entry)
				{
					case 0: provider.DisposeAsync().GetAwaiter().GetResult(); break;
					case 1: provider.Store.Dispose(); break;
					case 2: provider.PrepareSemanticFixtureClose(); break;
				}
			}
			catch (Exception error) { refusal = error; }
			tx.Put(Tables.Meta, Keys.Str("issued-callback-refusal"), [42]);
		}
		// A read callback cannot run writes inline; enqueue while that read owns its gate.
		if (queued) await provider.Store.Read(_ => provider.Store.WriteAsync(Write).AsTask());
		else await provider.Store.WriteAsync(Write);
		await Assert.That(refusal is InvalidOperationException).IsTrue();
		await Assert.That(owner.IsQuarantined).IsFalse();
		await using (var retained = await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None))
			await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(reopened.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("issued-callback-refusal"), out var value) && value.SequenceEqual(new byte[] { 42 }))).IsTrue();
	}

}
