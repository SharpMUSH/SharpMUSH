using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;

namespace SharpMUSH.Server.Mcp;

/// <summary>Compatibility adapter over the host's shared cooperative analysis pool.</summary>
public sealed class McpAnalysisExecutor : IDisposable
{
	private readonly AnalysisExecutor _executor;
	private readonly bool _owns;
	public McpAnalysisExecutor(AnalysisExecutor executor) => _executor = executor;
	public McpAnalysisExecutor(IOptions<McpOptions> options, IHostApplicationLifetime? lifetime = null)
	{
		var value = options.Value;
		_executor = new AnalysisExecutor(Options.Create(new AnalysisExecutionOptions
		{
			MaxConcurrentAnalyses = value.MaxConcurrentAnalyses, MaxQueuedAnalyses = value.MaxQueuedAnalyses, AnalysisTimeout = value.AnalysisTimeout
		}), lifetime?.ApplicationStopping ?? default);
		_owns = true;
	}
	public Task<T> ExecuteAsync<T>(Func<T> work, CancellationToken cancellationToken = default) => _executor.ExecuteAsync(() => Task.FromResult(work()), cancellationToken);
	public Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) => _executor.ExecuteAsync(work, cancellationToken);
	public void Dispose() { if (_owns) _executor.Dispose(); }
}
