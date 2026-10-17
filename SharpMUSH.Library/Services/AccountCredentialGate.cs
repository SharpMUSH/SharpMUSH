namespace SharpMUSH.Library.Services;

/// <summary>
/// Serializes local credential/ownership writes with native authorization and final bind. Provider calls
/// may re-enter the current decision; callers must not launch detached work inside a decision.
/// </summary>
public sealed class AccountCredentialGate
{
	private sealed class Scope { public bool Active { get; set; } = true; }
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly AsyncLocal<Scope?> _current = new();

	public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
	{
		if (_current.Value is { Active: true }) return await action();
		await _gate.WaitAsync(cancellationToken);
		var previous = _current.Value;
		var scope = new Scope();
		_current.Value = scope;
		try { return await action(); }
		finally
		{
			// A child execution context retained by queued work cannot keep authority after this decision.
			scope.Active = false;
			_current.Value = previous;
			_gate.Release();
		}
	}

	public Task RunAsync(Func<Task> action, CancellationToken cancellationToken = default)
		=> RunAsync(async () => { await action(); return true; }, cancellationToken);
}
