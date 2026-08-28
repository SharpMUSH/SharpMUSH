using System.Collections.Concurrent;
using SharpMUSH.Database;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// The hazard <see cref="FreshAsyncEnumerable{T}"/> exists to remove, and proof that it removes it.
///
/// <para>Every <c>SharpObject</c> property that streams (Flags, Powers, the four attribute
/// collections, Children) is a <see cref="Lazy{T}"/> over one <see cref="IAsyncEnumerable{T}"/>
/// instance which every call site then enumerates. When the iterator method's own state machine is
/// that instance, two enumerations can end up sharing it, and the second one either crashes the first
/// one's disposal or gets silently cut short. Issue #798 is the crash; the truncation has no symptom
/// at all beyond a short list.</para>
///
/// <para>These tests drive continuations through a single-threaded pump, because the fast path that
/// hands out the shared instance is guarded by <c>state == initial &amp;&amp; threadId ==
/// creatingThreadId</c>. On the thread pool that guard holds by coincidence — a pooled thread handed
/// back under the same managed id — which is why the failure is load-dependent and rare rather than
/// impossible.</para>
/// </summary>
public class FreshAsyncEnumerableTests
{
	/// <summary>Runs <paramref name="scenario"/> with every continuation resuming on one thread.</summary>
	private static void OnOneThread(Func<Task> scenario)
	{
		var previous = SynchronizationContext.Current;
		var pump = new SingleThreadContext();
		try
		{
			SynchronizationContext.SetSynchronizationContext(pump);
			scenario().ContinueWith(_ => pump.Complete(), TaskScheduler.FromCurrentSynchronizationContext());
			pump.Run();
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}
	}

	private sealed class SingleThreadContext : SynchronizationContext
	{
		private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
		public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
		public void Run()
		{
			foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
		}
		public void Complete() => _queue.CompleteAdding();
	}

	private static async IAsyncEnumerable<int> Numbers()
	{
		for (var i = 0; i < 3; i++)
		{
			// An await inside the body, as every real provider query has: it is the suspension point
			// that leaves the state machine "running" when a stale consumer disposes it.
			await Task.Yield();
			yield return i;
		}
	}

	[Test]
	public async Task RawIterator_HandsTheSameStateMachineToASecondEnumeration()
	{
		var sameObject = false;
		Exception? disposal = null;

		OnOneThread(async () =>
		{
			IAsyncEnumerable<int> shared = Numbers();

			var first = shared.GetAsyncEnumerator();
			while (await first.MoveNextAsync()) { }

			var second = shared.GetAsyncEnumerator();
			sameObject = ReferenceEquals(first, second);

			var inFlight = second.MoveNextAsync();
			try
			{
				await first.DisposeAsync();
			}
			catch (Exception ex)
			{
				disposal = ex;
			}

			await inFlight;
		});

		await Assert.That(sameObject).IsTrue();
		// The exact failure in #798: NotSupportedException("Specified method is not supported.") out of
		// the compiler-generated DisposeAsync, because the machine it was asked to dispose is running.
		await Assert.That(disposal).IsTypeOf<NotSupportedException>();
	}

	[Test]
	public async Task FreshAsyncEnumerable_GivesEachEnumerationItsOwnStateMachine()
	{
		var sameObject = true;
		Exception? disposal = null;

		OnOneThread(async () =>
		{
			IAsyncEnumerable<int> shared = new FreshAsyncEnumerable<int>(Numbers);

			var first = shared.GetAsyncEnumerator();
			while (await first.MoveNextAsync()) { }

			var second = shared.GetAsyncEnumerator();
			sameObject = ReferenceEquals(first, second);

			var inFlight = second.MoveNextAsync();
			try
			{
				await first.DisposeAsync();
			}
			catch (Exception ex)
			{
				disposal = ex;
			}

			await inFlight;
		});

		await Assert.That(sameObject).IsFalse();
		await Assert.That(disposal).IsNull();
	}

	[Test]
	public async Task FreshAsyncEnumerable_ReplaysInFullOnEveryEnumeration()
	{
		var calls = 0;
		IAsyncEnumerable<int> shared = new FreshAsyncEnumerable<int>(() =>
		{
			calls++;
			return Numbers();
		});

		await Assert.That(await shared.ToArrayAsync()).IsEquivalentTo(new[] { 0, 1, 2 });
		await Assert.That(await shared.ToArrayAsync()).IsEquivalentTo(new[] { 0, 1, 2 });
		await Assert.That(calls).IsEqualTo(2);
	}

	/// <summary>
	/// A short-circuiting consumer — <c>AnyAsync</c>, which is what <c>HasFlag</c> uses — abandons the
	/// enumeration mid-stream. Interleaved with other consumers of the same cached enumerable, that is
	/// where the disposal lands on someone else's state machine.
	/// </summary>
	[Test]
	public async Task FreshAsyncEnumerable_SurvivesInterleavedShortCircuitingConsumers()
	{
		IAsyncEnumerable<int> shared = new FreshAsyncEnumerable<int>(Numbers);

		var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(async i =>
		{
			await Task.Yield();
			return i % 2 == 0
				? await shared.AnyAsync(x => x == 0)
				: await shared.AnyAsync(x => x == 2);
		}));

		await Assert.That(results.All(x => x)).IsTrue();
	}
}
