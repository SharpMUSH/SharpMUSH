using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// <see cref="SingleFlight{TKey,TValue}"/> coalesces concurrent identical requests and nothing else:
/// callers that overlap share one invocation, a call after the shared one has finished starts a new
/// one, a failure is never kept, and one caller giving up does not end the request for the others.
/// </summary>
public class SingleFlightTests
{
	[Test]
	public async Task ConcurrentCalls_ForOneKey_InvokeTheFactoryOnce()
	{
		var flight = new SingleFlight<string, int>();
		var gate = new TaskCompletionSource<int>();
		var invocations = 0;
		Task<int> Fetch()
		{
			invocations++;
			return gate.Task;
		}

		var first = flight.RunAsync("k", Fetch);
		var second = flight.RunAsync("k", Fetch);
		gate.SetResult(42);

		await Assert.That(await first).IsEqualTo(42);
		await Assert.That(await second).IsEqualTo(42);
		await Assert.That(invocations).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentCalls_ForDifferentKeys_InvokeTheFactoryForEach()
	{
		var flight = new SingleFlight<string, string>();
		var gate = new TaskCompletionSource();
		var invocations = 0;
		Func<Task<string>> Fetch(string value) => async () =>
		{
			invocations++;
			await gate.Task;
			return value;
		};

		var a = flight.RunAsync("a", Fetch("A"));
		var b = flight.RunAsync("b", Fetch("B"));
		gate.SetResult();

		await Assert.That(await a).IsEqualTo("A");
		await Assert.That(await b).IsEqualTo("B");
		await Assert.That(invocations).IsEqualTo(2);
	}

	[Test]
	public async Task SequentialCalls_EachInvokeTheFactory()
	{
		var flight = new SingleFlight<string, int>();
		var invocations = 0;
		async Task<int> Fetch()
		{
			await Task.Yield();
			return ++invocations;
		}

		await Assert.That(await flight.RunAsync("k", Fetch)).IsEqualTo(1);
		await Assert.That(await flight.RunAsync("k", Fetch)).IsEqualTo(2);
	}

	[Test]
	public async Task SynchronouslyCompletingFactory_IsNotKept()
	{
		var flight = new SingleFlight<string, int>();
		var invocations = 0;

		await flight.RunAsync("k", () => Task.FromResult(++invocations));
		await flight.RunAsync("k", () => Task.FromResult(++invocations));

		await Assert.That(invocations).IsEqualTo(2);
	}

	[Test]
	public async Task Failure_ReachesEveryConcurrentCaller_AndIsNotKept()
	{
		var flight = new SingleFlight<string, int>();
		var gate = new TaskCompletionSource<int>();
		var invocations = 0;

		var first = flight.RunAsync("k", () => { invocations++; return gate.Task; });
		var second = flight.RunAsync("k", () => { invocations++; return gate.Task; });
		gate.SetException(new InvalidOperationException("boom"));

		await Assert.That(async () => await first).Throws<InvalidOperationException>();
		await Assert.That(async () => await second).Throws<InvalidOperationException>();

		var retry = await flight.RunAsync("k", () => { invocations++; return Task.FromResult(7); });
		await Assert.That(retry).IsEqualTo(7);
		await Assert.That(invocations).IsEqualTo(2);
	}

	[Test]
	public async Task SynchronouslyThrowingFactory_FaultsTheTask_AndIsNotKept()
	{
		var flight = new SingleFlight<string, int>();

		var failed = flight.RunAsync("k", () => throw new InvalidOperationException("sync"));

		await Assert.That(async () => await failed).Throws<InvalidOperationException>();
		await Assert.That(await flight.RunAsync("k", () => Task.FromResult(3))).IsEqualTo(3);
	}

	[Test]
	public async Task OneCallerCancelling_DoesNotCancelTheSharedRequest()
	{
		var flight = new SingleFlight<string, int>();
		var gate = new TaskCompletionSource<int>();
		using var cts = new CancellationTokenSource();

		var abandoned = flight.RunAsync("k", () => gate.Task, cts.Token);
		var kept = flight.RunAsync("k", () => gate.Task);
		await cts.CancelAsync();

		await Assert.That(async () => await abandoned).Throws<OperationCanceledException>();
		await Assert.That(kept.IsCompleted).IsFalse();

		gate.SetResult(9);
		await Assert.That(await kept).IsEqualTo(9);
	}

	[Test]
	public async Task AlreadyCancelledToken_DoesNotStartARequest()
	{
		var flight = new SingleFlight<string, int>();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		var invocations = 0;

		var call = flight.RunAsync("k", () => Task.FromResult(++invocations), cts.Token);

		await Assert.That(async () => await call).Throws<OperationCanceledException>();
		await Assert.That(invocations).IsEqualTo(0);
	}

	[Test]
	public async Task KeyComparer_IsHonoured()
	{
		var flight = new SingleFlight<string, int>(StringComparer.OrdinalIgnoreCase);
		var gate = new TaskCompletionSource<int>();
		var invocations = 0;

		var lower = flight.RunAsync("home", () => { invocations++; return gate.Task; });
		var upper = flight.RunAsync("HOME", () => { invocations++; return gate.Task; });
		gate.SetResult(1);
		await Task.WhenAll(lower, upper);

		await Assert.That(invocations).IsEqualTo(1);
	}
}
