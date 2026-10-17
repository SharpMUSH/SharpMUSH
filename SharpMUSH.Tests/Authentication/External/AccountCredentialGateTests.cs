using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Authentication.External;

public class AccountCredentialGateTests
{
	[Test]
	public async Task ConcurrentDecisionsNeverOverlapAndNestedProviderWritesCanReenter()
	{
		var gate = new AccountCredentialGate();
		var active = 0;
		var maximum = 0;
		await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => gate.RunAsync(async () =>
		{
			var count = Interlocked.Increment(ref active);
			maximum = Math.Max(maximum, count);
			await Task.Yield();
			await gate.RunAsync(async () => { await Task.Yield(); });
			Interlocked.Decrement(ref active);
		})));
		await Assert.That(maximum).IsEqualTo(1);
	}

	[Test]
	public async Task FailureAndCancelledWaitDoNotKeepTheGateHeld()
	{
		var gate = new AccountCredentialGate();
		try { await gate.RunAsync<int>(() => throw new InvalidOperationException("expected")); }
		catch (InvalidOperationException) { }
		await Assert.That(await gate.RunAsync(() => Task.FromResult(7))).IsEqualTo(7);
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		try { await gate.RunAsync(() => Task.CompletedTask, cancelled.Token); }
		catch (OperationCanceledException) { }
		await Assert.That(await gate.RunAsync(() => Task.FromResult(9))).IsEqualTo(9);
	}
}
