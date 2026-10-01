namespace SharpMUSH.Tests;

/// <summary>
/// Runs a piece of session-wide fixture setup exactly once, however many tests reach it together.
/// Held in a <c>static readonly</c> field of the test class: the tests share one world, so the setup
/// belongs to the session rather than to any one test. A setup that throws is retried by the next caller.
/// </summary>
public sealed class RunOnce
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private bool _done;

	public async Task RunAsync(Func<Task> setup)
	{
		await _gate.WaitAsync();
		try
		{
			if (_done) return;
			await setup();
			_done = true;
		}
		finally
		{
			_gate.Release();
		}
	}
}
