using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Tests;

/// <summary>
/// Test-owned host lifetime. Register as an implementation instance: the inner host disposes its DI
/// container, so making this owner a container-owned singleton would introduce self-disposal recursion.
/// </summary>
internal sealed class TestHostLifetime(Func<IServiceProvider, IHost> createHost) : IHost, IAsyncDisposable
{
	private readonly object _gate = new();
	private IServiceProvider? _provider;
	private IHost? _inner;
	private Task? _start;
	private Task? _stop;
	private Task? _dispose;

	internal void Bind(IServiceProvider provider)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_dispose is not null, this);
			if (_provider is not null && !ReferenceEquals(_provider, provider))
				throw new InvalidOperationException("A test host cannot bind to another service provider.");
			_provider = provider;
		}
	}

	public IServiceProvider Services
	{
		get
		{
			lock (_gate)
			{
				ObjectDisposedException.ThrowIf(_dispose is not null, this);
				return Inner().Services;
			}
		}
	}

	// Caller holds the gate. Construction is synchronous; no lifecycle await holds the lock.
	private IHost Inner() => _inner ??= createHost(_provider
		?? throw new InvalidOperationException("The test host must bind through configuration before use."));

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		TaskCompletionSource? operation = null;
		Task result;
		IHost inner;
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_dispose is not null || _stop is not null, this);
			inner = Inner();
			if (_start is null) _start = (operation = Completion()).Task;
			result = _start;
		}
		if (operation is not null) _ = CompleteAsync(operation, () => inner.StartAsync(cancellationToken));
		return result;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			// A disposal already in progress owns shutdown. Join it rather than stop disposed services.
			if (_dispose is not null) return _dispose;
		}
		return EnsureStop(cancellationToken);
	}

	private Task EnsureStop(CancellationToken cancellationToken)
	{
		TaskCompletionSource? operation = null;
		Task result;
		Task? start;
		IHost? inner;
		lock (_gate)
		{
			if (_stop is null) _stop = (operation = Completion()).Task;
			result = _stop;
			start = _start;
			inner = _inner;
		}
		if (operation is not null) _ = CompleteAsync(operation, async () =>
		{
			if (start is not null)
			{
				// Drain startup before cleanup. Its failure belongs to StartAsync's caller; cleanup
				// must still stop/dispose the owner, and its own failures remain observable.
				try { await start.ConfigureAwait(false); }
				catch { }
			}
			if (inner is not null) await inner.StopAsync(cancellationToken).ConfigureAwait(false);
		});
		return result;
	}

	public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

	public ValueTask DisposeAsync()
	{
		TaskCompletionSource? operation = null;
		Task result;
		lock (_gate)
		{
			if (_dispose is null) _dispose = (operation = Completion()).Task;
			result = _dispose;
		}
		if (operation is not null) _ = CompleteAsync(operation, DisposeInnerAsync);
		return new ValueTask(result);
	}

	private async Task DisposeInnerAsync()
	{
		Exception? stopFailure = null;
		try { await EnsureStop(CancellationToken.None).ConfigureAwait(false); }
		catch (Exception error) { stopFailure = error; }
		object? owner;
		lock (_gate) owner = (object?)_inner ?? _provider;
		try
		{
			// Construction can fail after resolving DI-owned resources but before returning a host.
			// Close that existing root directly; never instantiate another host during cleanup.
			if (owner is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
			else if (owner is IDisposable synchronous) synchronous.Dispose();
		}
		catch (Exception error) when (stopFailure is not null)
		{
			throw new AggregateException("Test host shutdown and disposal both failed.", stopFailure, error);
		}
		if (stopFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stopFailure).Throw();
	}

	private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static async Task CompleteAsync(TaskCompletionSource completion, Func<Task> operation)
	{
		try { await operation().ConfigureAwait(false); completion.TrySetResult(); }
		catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
		catch (Exception error) { completion.TrySetException(error); }
	}
}
