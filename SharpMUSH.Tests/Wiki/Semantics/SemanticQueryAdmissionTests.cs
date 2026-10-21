using SharpMUSH.Database.Lightning.Semantics;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// The wiki-query admission queue (spec §9): a few queries run at once across the game and one per principal,
/// a few more wait in order, and anything past that is refused before it touches storage. Administrators count
/// like everyone else.
/// </summary>
public class SemanticQueryAdmissionTests
{
	private sealed class ManualClock : TimeProvider
	{
		internal long Ticks { get; set; }
		public override long TimestampFrequency => TimeSpan.TicksPerSecond;
		public override long GetTimestamp() => Ticks;
	}

	private static readonly SemanticQueryAdmissionLimits Limits = new(Active: 2, ActivePerPrincipal: 1, QueuedPerPrincipal: 2, Queued: 4);

	private static SemanticQueryAdmission.Ticket Admitted(ValueTask<SemanticQueryAdmission.Entry> entry)
		=> entry.IsCompletedSuccessfully && entry.Result is SemanticQueryAdmission.Entry.Admitted admitted
			? admitted.Ticket : throw new InvalidOperationException("Expected an immediate admission.");

	private static async Task<string> Refused(ValueTask<SemanticQueryAdmission.Entry> entry)
		=> entry.IsCompletedSuccessfully && entry.Result is SemanticQueryAdmission.Entry.Refused refused
			? refused.Limit : throw new InvalidOperationException($"Expected an immediate refusal, got {(entry.IsCompleted ? await entry : "a wait")}.");

	[Test]
	public async Task OneQueryPerPrincipalAndTwoAcrossTheGameRunAtOnce()
	{
		var admission = new SemanticQueryAdmission(Limits, new ManualClock());
		using var a = Admitted(admission.EnterAsync("#5", CancellationToken.None));
		var secondFromSame = admission.EnterAsync("#5", CancellationToken.None);
		await Assert.That(secondFromSame.IsCompleted).IsFalse();
		using var b = Admitted(admission.EnterAsync("#6", CancellationToken.None));
		var third = admission.EnterAsync("#7", CancellationToken.None);
		await Assert.That(third.IsCompleted).IsFalse();
		await Assert.That(admission.Snapshot()).IsEqualTo(new SemanticQueryAdmissionStats(Active: 2, Queued: 2, Refused: 0, LongestWait: TimeSpan.Zero));
	}

	[Test]
	public async Task APrincipalPastItsQueueAndAGamePastItsQueueAreRefusedByName()
	{
		var admission = new SemanticQueryAdmission(Limits, new ManualClock());
		using var running = Admitted(admission.EnterAsync("#5", CancellationToken.None));
		using var other = Admitted(admission.EnterAsync("#6", CancellationToken.None));
		var waiting = new[] { admission.EnterAsync("#5", CancellationToken.None), admission.EnterAsync("#5", CancellationToken.None) };
		await Assert.That(await Refused(admission.EnterAsync("#5", CancellationToken.None))).IsEqualTo("principal");
		// The administrator is one more principal, under the same limits.
		waiting = [.. waiting, admission.EnterAsync("#1", CancellationToken.None), admission.EnterAsync("#7", CancellationToken.None)];
		await Assert.That(await Refused(admission.EnterAsync("#8", CancellationToken.None))).IsEqualTo("queue");
		await Assert.That(waiting.All(w => !w.IsCompleted)).IsTrue();
		await Assert.That(admission.Snapshot().Refused).IsEqualTo(2);
	}

	[Test]
	public async Task AFreedSlotGoesToTheOldestWaiterWhosePrincipalIsNotRunning()
	{
		var clock = new ManualClock();
		var admission = new SemanticQueryAdmission(Limits, clock);
		var first = Admitted(admission.EnterAsync("#5", CancellationToken.None));
		var second = Admitted(admission.EnterAsync("#6", CancellationToken.None));
		var sameAsSecond = admission.EnterAsync("#6", CancellationToken.None);
		var later = admission.EnterAsync("#7", CancellationToken.None);
		clock.Ticks += TimeSpan.FromMilliseconds(40).Ticks;
		// #6 still runs, so its waiter is passed over and #7 takes #5's slot.
		first.Dispose();
		await Assert.That(sameAsSecond.IsCompleted).IsFalse();
		var seventh = (await later) as SemanticQueryAdmission.Entry.Admitted;
		await Assert.That(seventh).IsNotNull();
		await Assert.That(seventh!.Ticket.Waited).IsEqualTo(TimeSpan.FromMilliseconds(40));
		second.Dispose();
		await Assert.That(await sameAsSecond is SemanticQueryAdmission.Entry.Admitted).IsTrue();
		await Assert.That(admission.Snapshot().LongestWait).IsEqualTo(TimeSpan.FromMilliseconds(40));
	}

	[Test]
	public async Task ACancelledWaiterLeavesTheQueueAndNeverRuns()
	{
		var admission = new SemanticQueryAdmission(Limits, new ManualClock());
		var running = Admitted(admission.EnterAsync("#5", CancellationToken.None));
		using var cancellation = new CancellationTokenSource();
		var waiter = admission.EnterAsync("#5", cancellation.Token);
		cancellation.Cancel();
		await Assert.That(async () => await waiter).Throws<OperationCanceledException>();
		await Assert.That(admission.Snapshot().Queued).IsEqualTo(0);
		running.Dispose();
		await Assert.That(admission.Snapshot().Active).IsEqualTo(0);
		// Its place in the principal's queue is free again.
		using var next = Admitted(admission.EnterAsync("#5", CancellationToken.None));
	}

	[Test]
	public async Task ATicketReleasesOnceHoweverOftenItIsDisposed()
	{
		var admission = new SemanticQueryAdmission(Limits, new ManualClock());
		var ticket = Admitted(admission.EnterAsync("#5", CancellationToken.None));
		using var other = Admitted(admission.EnterAsync("#6", CancellationToken.None));
		ticket.Dispose();
		ticket.Dispose();
		await Assert.That(admission.Snapshot().Active).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentEntriesNeverExceedTheLimits()
	{
		var admission = new SemanticQueryAdmission(SemanticQueryAdmissionLimits.Default, new ManualClock());
		var peak = 0;
		var running = 0;
		var peakByPrincipal = new int[8];
		var runningByPrincipal = new int[8];
		await Parallel.ForAsync(0, 400, async (i, ct) =>
		{
			var principal = i % 8;
			if (await admission.EnterAsync($"#{principal}", ct) is not SemanticQueryAdmission.Entry.Admitted admitted) return;
			using (admitted.Ticket)
			{
				var now = Interlocked.Increment(ref running);
				var mine = Interlocked.Increment(ref runningByPrincipal[principal]);
				InterlockedMax(ref peak, now);
				InterlockedMax(ref peakByPrincipal[principal], mine);
				await Task.Yield();
				Interlocked.Decrement(ref runningByPrincipal[principal]);
				Interlocked.Decrement(ref running);
			}
		});
		await Assert.That(peak).IsLessThanOrEqualTo(SemanticQueryAdmissionLimits.Default.Active);
		await Assert.That(peakByPrincipal.Max()).IsLessThanOrEqualTo(SemanticQueryAdmissionLimits.Default.ActivePerPrincipal);
		var stats = admission.Snapshot();
		await Assert.That(stats.Active + stats.Queued).IsEqualTo(0);
	}

	private static void InterlockedMax(ref int target, int value)
	{
		var seen = Volatile.Read(ref target);
		while (value > seen)
		{
			var previous = Interlocked.CompareExchange(ref target, value, seen);
			if (previous == seen) return;
			seen = previous;
		}
	}
}
