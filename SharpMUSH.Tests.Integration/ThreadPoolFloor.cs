namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Raises the thread pool's floor before any test runs. The analysis transport tests hold analysis
/// on a gate, which parks a pool thread the way slow synchronous analysis does, while the server
/// they test answers on other pool threads within deadlines of a few hundred milliseconds to a few
/// seconds. The floor defaults to the processor count, and past it the pool adds a thread only every
/// half second or so, so a few gated tests running together on a small runner stall every server
/// past its deadline. Production bounds the same work with <c>MaxConcurrentAnalyses</c>.
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
