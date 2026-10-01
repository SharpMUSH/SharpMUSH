using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

public static class TaskSchedulerTestExtensions
{
	/// <summary>
	/// Waits for everything already in the immediate queue to run, and for <paramref name="rounds"/>
	/// generations of work that work queued in turn.
	///
	/// <para>The queue has one reader and runs entries in admission order, so a sentinel admitted now
	/// finishes after every entry admitted before it. Unlike
	/// <see cref="ITaskScheduler.DrainImmediateQueueForTests"/>, which waits for the whole session's
	/// queue to fall quiet, this never waits on work other tests admit afterwards, so it holds while
	/// tests run in parallel.</para>
	/// </summary>
	public static async Task SettleForTestsAsync(this ITaskScheduler scheduler, int rounds = 3, TimeSpan? timeout = null)
	{
		for (var round = 0; round < rounds; round++)
		{
			var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var admitted = await scheduler.AdmitWork(() =>
			{
				ran.TrySetResult();
				return ValueTask.FromResult<CallState?>(null);
			}, "test-settle", "test");
			if (!admitted.Accepted)
			{
				throw new InvalidOperationException($"The settle sentinel was refused: {admitted.Reason}");
			}

			await ran.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30));
		}
	}
}
