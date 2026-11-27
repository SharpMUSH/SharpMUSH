namespace SharpMUSH.Tests;

/// <summary>
/// Waits for what a command set going, as a player would before typing the next line: the queue, and then any
/// <c>@wait</c> due within <see cref="SoonDelay"/> (a package's <c>@wait 0</c> follow-up), with the queue again after
/// it runs. Longer waits are left for the test to wait on.
/// </summary>
/// <param name="barrier">Returns once everything admitted to the queue before it was called has run.</param>
/// <param name="delays">
/// Read in one step: when the soonest <c>@wait</c> not yet on the queue is due, and whether one is on the queue and
/// not finished (<see cref="ServerWebAppFactory.Delays"/>).
/// </param>
public sealed class QueueSettler(Func<Task> barrier, Func<(DateTimeOffset? NextDue, bool Released)> delays, TimeProvider clock)
{
	public static readonly TimeSpan SoonDelay = TimeSpan.FromMilliseconds(500);
	public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	public async Task SettleAsync()
	{
		var deadline = clock.GetUtcNow() + Limit;
		await barrier();
		while (true)
		{
			switch (delays())
			{
				// Released after the barrier was admitted, so it runs after it: the next barrier waits for it.
				case { Released: true }:
					break;
				case { NextDue: { } due } when due <= clock.GetUtcNow() + SoonDelay:
					var until = due - clock.GetUtcNow();
					await Task.Delay(until > TimeSpan.Zero ? until + SoonDelay / 10 : SoonDelay / 10, clock);
					break;
				default:
					return;
			}
			if (clock.GetUtcNow() >= deadline) break;
			await barrier();
		}
		throw new TimeoutException($"The queue did not settle within {Limit.TotalSeconds:0} seconds: a @wait due within {SoonDelay.TotalMilliseconds:0} ms was still pending or running.");
	}
}
