using Microsoft.Extensions.Options;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.CodeAnalysis;

public class AnalysisExecutionOptions
{
	public int MaxConcurrentAnalyses { get; set; } = 2;
	public int MaxQueuedAnalyses { get; set; } = 8;
	public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Bounded admission. A cancelled response never frees a worker that is still running.</summary>
public sealed class AnalysisExecutor : IDisposable
{
	private readonly object _gate = new();
	private readonly AnalysisExecutionOptions _options;
	private readonly SemaphoreSlim _workers;
	private readonly CancellationTokenSource _stopping = new();
	private readonly CancellationTokenRegistration _hostStopping;
	private int _admitted;
	private bool _disposed;

	public AnalysisExecutor(IOptions<AnalysisExecutionOptions> options, CancellationToken stopping = default)
	{
		_options = options.Value;
		if (_options.MaxConcurrentAnalyses <= 0 || _options.MaxQueuedAnalyses < 0 ||
			(long)_options.MaxConcurrentAnalyses + _options.MaxQueuedAnalyses > int.MaxValue ||
			_options.AnalysisTimeout <= TimeSpan.Zero || _options.AnalysisTimeout.TotalMilliseconds > uint.MaxValue - 1)
			throw new ArgumentException("Analysis limits and elapsed timeout must be positive and finite.", nameof(options));
		_workers = new SemaphoreSlim(_options.MaxConcurrentAnalyses);
		_hostStopping = stopping.Register(() => _stopping.Cancel());
	}

	public async Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_admitted >= _options.MaxConcurrentAnalyses + _options.MaxQueuedAnalyses)
				throw new InvalidOperationException("Analysis capacity exceeded. Retry later.");
			_admitted++;
		}
		try
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token, ExecutionBudget.CurrentToken);
			var remaining = ExecutionBudget.Current?.Remaining ?? TimeSpan.MaxValue;
			using var budget = new ExecutionBudget(remaining < _options.AnalysisTimeout ? remaining : _options.AnalysisTimeout, linked.Token);
			await _workers.WaitAsync(budget.Token);
			try
			{
				// Never use WaitAsync(timeout) around this task: that would abandon live work and release
				// its slot. Await its actual termination, with cancellation checked inside syntax work.
				return await Task.Run(async () =>
				{
					using var scope = budget.Enter();
					budget.ThrowIfExceeded();
					var result = await work();
					budget.ThrowIfExceeded();
					return result;
				}, CancellationToken.None);
			}
			finally { _workers.Release(); }
		}
		finally
		{
			lock (_gate)
			{
				_admitted--;
				if (_disposed && _admitted == 0) DisposeResources();
			}
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed) return;
			_disposed = true;
			_stopping.Cancel();
			if (_admitted == 0) DisposeResources();
		}
	}

	private void DisposeResources()
	{
		_hostStopping.Dispose();
		_workers.Dispose();
		_stopping.Dispose();
	}
}
