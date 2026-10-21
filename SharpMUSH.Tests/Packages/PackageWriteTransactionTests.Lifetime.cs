using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

public partial class PackageWriteTransactionTests
{
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task TerminalTransactionRejectsSourceWritesAndLateUndoRegistration(int terminal)
	{
		var (_, dbref, _) = await SubjectAsync("terminal transaction " + Guid.NewGuid().ToString("N"), "tx-terminal-" + Guid.NewGuid().ToString("N"));
		var writes = await BeginAsync();
		try
		{
			if (terminal == 0) writes.Commit();
			else if (terminal == 1) await writes.DisposeAsync();
			else await writes.RevertAsync(new Error<string>("original failure"));
			await Assert.That(async () => await writes.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("late"), await PackageManagerAsync(), CancellationToken.None))
				.Throws<InvalidOperationException>();
			await Assert.That(() => writes.Track("late undo", () => Task.CompletedTask)).Throws<InvalidOperationException>();
			await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("original");
		}
		finally { await writes.DisposeAsync(); await Mediator.Send(new DeleteObjectCommand(dbref)); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RepeatedDisposalJoinsActualUndoUntilItFinishes(bool explicitRevert)
	{
		var writes = await BeginAsync();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var undoFinished = false;
		writes.Track("owned paused inverse", async () => { started.TrySetResult(); await release.Task; undoFinished = true; });
		Task? first = null;
		Task? second = null;
		try
		{
			first = explicitRevert ? writes.RevertAsync(new Error<string>("failure")) : writes.DisposeAsync().AsTask();
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			second = writes.DisposeAsync().AsTask();
			await Assert.That(second.IsCompleted).IsFalse();
			await Assert.That(undoFinished).IsFalse();
			await Assert.That(() => writes.Commit()).Throws<InvalidOperationException>();
		}
		finally
		{
			release.TrySetResult();
			if (first is not null) await first;
			if (second is not null) await second;
		}
		await Assert.That(undoFinished).IsTrue();
	}

	[Test]
	[NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CloseJoinsAdmittedNativeWriteAndCommitCannotForgetItsUndo(bool commitWhilePending)
	{
		var (_, dbref, _) = await SubjectAsync("pending transaction " + Guid.NewGuid().ToString("N"), "tx-pending-" + Guid.NewGuid().ToString("N"));
		var writes = await BeginAsync();
		var provider = WebAppFactoryArg.Services.GetRequiredService<LightningDatabase>();
		var pm = await PackageManagerAsync();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new ManualResetEventSlim(false);
		var blocker = Task.Run(async () => await provider.Store.WriteAsync(_ =>
		{
			started.TrySetResult();
			if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Owned transaction writer barrier");
		}));
		Task? forward = null;
		Task? second = null;
		Task? close = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			forward = writes.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("changed"), pm, CancellationToken.None);
			await Assert.That(forward.IsCompleted).IsFalse();
			if (commitWhilePending)
			{
				second = writes.ClearAttributeAsync(dbref, ["TX_VALUE"], CancellationToken.None);
				await Assert.That(second.IsCompleted).IsTrue();
				await Assert.That(async () => await second).Throws<InvalidOperationException>();
				await Assert.That(() => writes.Commit()).Throws<InvalidOperationException>();
			}
			close = writes.DisposeAsync().AsTask();
			await Assert.That(close.IsCompleted).IsFalse();
		}
		finally
		{
			release.Set();
			await blocker;
			if (forward is not null) await forward;
			if (second is not null)
			{
				try { await second; } catch (InvalidOperationException) { }
			}
			if (close is not null) await close;
			else await writes.DisposeAsync();
		}
		try { await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("original"); }
		finally { await Mediator.Send(new DeleteObjectCommand(dbref)); }
	}

	[Test]
	public async Task RepeatedImplicitDisposalPreservesActualCompensationFailure()
	{
		var writes = await BeginAsync();
		var attempted = 0;
		writes.Track("owned failed inverse", () => { attempted++; throw new ArithmeticException("owned inverse failure"); });
		await Assert.That(async () => await writes.DisposeAsync()).Throws<PackageCompensationException>();
		await Assert.That(async () => await writes.DisposeAsync()).Throws<PackageCompensationException>();
		await Assert.That(attempted).IsEqualTo(1);
	}
}
