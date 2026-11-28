namespace SharpMUSH.Tests;

/// <summary>
/// Raises the thread pool's floor before any test runs. Tests run eight at a time against hosts that
/// answer NATS requests on pool threads within NATS.Net's 5 s request timeout; when the tests park the
/// pool's threads, the floor defaults to the processor count and past it the pool adds a thread only
/// every half second or so. On a 4-core runner a full run saw work items wait 8.7 s with 84 queued
/// and 7 threads, long enough for an in-flight request to fail with no reply (#2785).
/// </summary>
public static class ThreadPoolFloor
{
	private const int MinimumWorkerThreads = 64;

	[Before(TestSession)]
	public static void Raise()
	{
		ThreadPool.GetMinThreads(out var workers, out var completionPorts);
		ThreadPool.SetMinThreads(Math.Max(workers, MinimumWorkerThreads), completionPorts);
	}
}
