using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

public class PackageReadIsolationGateTests
{
	[Test]
	public async Task AReadInsideAnUnfinishedOperationIsRejectedBeforeItsCallback()
	{
		var gate = new PackageOperationGate();
		var ran = false;
		await gate.RunAsync(async () =>
		{
			await Assert.That(async () => await ((IPackageReadIsolationGate)gate).RunIsolatedReadAsync(() =>
			{
				ran = true;
				return Task.FromResult(1);
			})).Throws<InvalidOperationException>();
			return 0;
		});
		await Assert.That(ran).IsFalse();
		await Assert.That(await gate.RunAsync(() => Task.FromResult(2))).IsEqualTo(2);
	}

	[Test]
	public async Task AnIsolatedReadCannotStartAPackageOperationOrAnotherRead()
	{
		var gate = new PackageOperationGate();
		var invoked = 0;
		await ((IPackageReadIsolationGate)gate).RunIsolatedReadAsync(async () =>
		{
			Task<int> Callback() { invoked++; return Task.FromResult(1); }
			await Assert.That(async () => await gate.RunAsync(Callback)).Throws<InvalidOperationException>();
			await Assert.That(async () => await ((IPackageReadIsolationGate)gate).RunIsolatedReadAsync(Callback))
				.Throws<InvalidOperationException>();
			return 0;
		});
		await Assert.That(invoked).IsEqualTo(0);
		await Assert.That(await gate.RunAsync(() => Task.FromResult(3))).IsEqualTo(3);
	}

	[Test]
	public async Task WaitingReadCancellationDoesNotEnterOrReleaseTheActiveOperation()
	{
		var gate = new PackageOperationGate();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var holder = gate.RunAsync(async () => { entered.SetResult(); await release.Task; return 0; });
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		using var cancelled = new CancellationTokenSource();
		var invoked = false;
		var waiting = ((IPackageReadIsolationGate)gate).RunIsolatedReadAsync(() =>
		{
			invoked = true;
			return Task.FromResult(1);
		}, cancelled.Token);
		try
		{
			await Assert.That(waiting.IsCompleted).IsFalse();
			cancelled.Cancel();
			await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
			await Assert.That(invoked).IsFalse();
			await Assert.That(holder.IsCompleted).IsFalse();
		}
		finally { release.TrySetResult(); await holder.WaitAsync(TimeSpan.FromSeconds(5)); }
	}

	[Test]
	public async Task CancellationAfterEntryKeepsExclusionUntilTheActualReadFinishes()
	{
		var gate = new PackageOperationGate();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancelled = new CancellationTokenSource();
		var reader = ((IPackageReadIsolationGate)gate).RunIsolatedReadAsync(async () =>
		{
			entered.SetResult();
			await release.Task;
			return 7;
		}, cancelled.Token);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancelled.Cancel();
		var operation = gate.RunAsync(() => Task.FromResult(8));
		try
		{
			await Assert.That(reader.IsCompleted).IsFalse();
			await Assert.That(operation.IsCompleted).IsFalse();
		}
		finally { release.TrySetResult(); }
		await Assert.That(await reader.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(7);
		await Assert.That(await operation.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(8);
	}

	[Test]
	public async Task AReadThatThrowsReleasesBothTheGateAndItsAsyncFlowMode()
	{
		var gate = new PackageOperationGate();
		await Assert.That(async () => await ((IPackageReadIsolationGate)gate).RunIsolatedReadAsync<int>(
			() => throw new InvalidOperationException("failed read"))).Throws<InvalidOperationException>();
		await Assert.That(await gate.RunAsync(() => Task.FromResult(9))).IsEqualTo(9);
		await Assert.That(await ((IPackageReadIsolationGate)gate).RunIsolatedReadAsync(() => Task.FromResult(10))).IsEqualTo(10);
	}

	[Test]
	public async Task OrdinaryOperationReentrancyIsPreserved()
	{
		var gate = new PackageOperationGate();
		var result = await gate.RunAsync(() => gate.RunAsync(() => Task.FromResult(11)))
			.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(result).IsEqualTo(11);
	}
}
