namespace SharpMUSH.Client.Services;

/// <summary>
/// Coalesces concurrent identical requests: while a request for a key is in flight, every other
/// caller for that key awaits the same task instead of starting its own. Nothing is cached — the
/// entry is dropped the moment the request finishes, successfully or not, so the next call (a
/// refresh timer, a reload after a mutation) always goes to the network.
/// </summary>
/// <remarks>
/// The home page renders several widgets that read the same endpoints on the same frame (the stats
/// tile and the online list both read <c>http/online</c>, and so on). The services are singletons,
/// so routing their reads through one of these turns N identical requests into one without changing
/// what any caller sees. The <c>/http/*</c> routes also draw on the server's softcode HTTP rate
/// limit, so a duplicate there is a step toward a 429 as well as wasted work.
/// <para>
/// The shared request runs without any caller's token: one caller navigating away must not cancel
/// the answer another caller is still waiting for. Each caller's token only ends that caller's wait.
/// </para>
/// </remarks>
public sealed class SingleFlight<TKey, TValue>(IEqualityComparer<TKey>? comparer = null) where TKey : notnull
{
	private readonly Dictionary<TKey, Task<TValue>> _inFlight = new(comparer);
	private readonly Lock _gate = new();

	/// <summary>
	/// Joins the request in flight for <paramref name="key"/>, or starts one with
	/// <paramref name="fetch"/> if there is none.
	/// </summary>
	/// <param name="cancellationToken">Ends this caller's wait only; the shared request carries on.</param>
	public Task<TValue> RunAsync(TKey key, Func<Task<TValue>> fetch, CancellationToken cancellationToken = default)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return Task.FromCanceled<TValue>(cancellationToken);
		}

		Task<TValue> shared;
		TaskCompletionSource<TValue>? owner = null;
		lock (_gate)
		{
			if (!_inFlight.TryGetValue(key, out shared!))
			{
				// Registered before the factory runs, so a factory that re-enters for the same key, or
				// completes synchronously, still sees (and then clears) a consistent entry.
				owner = new TaskCompletionSource<TValue>();
				shared = owner.Task;
				_inFlight[key] = shared;
			}
		}

		if (owner is not null)
		{
			_ = ExecuteAsync(key, fetch, owner);
		}

		return cancellationToken.CanBeCanceled ? shared.WaitAsync(cancellationToken) : shared;
	}

	private async Task ExecuteAsync(TKey key, Func<Task<TValue>> fetch, TaskCompletionSource<TValue> owner)
	{
		Exception? failure = null;
		var value = default(TValue)!;
		try
		{
			value = await fetch();
		}
		catch (Exception ex)
		{
			failure = ex;
		}

		// Forget the request before completing it, so a caller whose continuation runs inline and
		// immediately asks again gets a fresh request rather than this finished one.
		lock (_gate)
		{
			_inFlight.Remove(key);
		}

		switch (failure)
		{
			case null:
				owner.SetResult(value);
				break;
			case OperationCanceledException canceled:
				owner.SetCanceled(canceled.CancellationToken);
				break;
			default:
				owner.SetException(failure);
				break;
		}
	}
}
