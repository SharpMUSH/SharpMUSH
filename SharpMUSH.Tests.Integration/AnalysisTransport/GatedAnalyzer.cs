using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

internal sealed class GatedAnalyzer : IMushCodeAnalyzer, IDisposable
{
	private readonly IMushCodeAnalyzer _inner = AnalysisCatalogAnalyzer.Create(BuiltInAnalysisCatalog.CreateSnapshot(), SharpMUSHOptions.Default());
	private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public ManualResetEventSlim Release { get; } = new();
	public bool GateValidation { get; init; }
	public bool GateProjection { get; init; }
	public bool FastValidation { get; init; }
	public bool ProjectionHasExecutionBudget { get; private set; }
	private string _projectionTrace = "Projection has not started.";
	private int _projectionTimedOut;

	public string DescribeProjection()
	{
		ThreadPool.GetAvailableThreads(out var workers, out var completionPorts);
		return $"Projection gate timed out: {Volatile.Read(ref _projectionTimedOut) != 0}; " +
			$"pool threads: {ThreadPool.ThreadCount}; available workers: {workers}; " +
			$"available completion ports: {completionPorts}; pending work: {ThreadPool.PendingWorkItemCount}.\n" +
			Volatile.Read(ref _projectionTrace);
	}

	/// <summary>
	/// Completes once the gate holds analysis. Awaited, never waited on: the gate already blocks one
	/// pool thread, and a test blocking another starves the server's reader loop of the threads it
	/// needs to answer.
	/// </summary>
	public Task Entered(TimeSpan timeout) => _entered.Task.WaitAsync(timeout);
	private sealed class ProjectionList(IReadOnlyList<Diagnostic> values, GatedAnalyzer owner) : IReadOnlyList<Diagnostic>
	{
		public int Count => values.Count;
		public Diagnostic this[int index] => values[index];
		public IEnumerator<Diagnostic> GetEnumerator()
		{
			// Capture before signalling the test: reader-path frames can identify synchronous projection
			// on that call path. Their absence is inconclusive. Pool counters alone cannot establish
			// starvation, so retain the actual projection stack too.
			Volatile.Write(ref owner._projectionTrace, $"Projection thread: {Environment.CurrentManagedThreadId}.\n{Environment.StackTrace}");
			owner.ProjectionHasExecutionBudget = ExecutionBudget.Current is not null;
			owner._entered.TrySetResult();
			if (!owner.Release.Wait(TimeSpan.FromSeconds(5)))
			{
				Volatile.Write(ref owner._projectionTimedOut, 1);
				throw new TimeoutException("Diagnostic projection gate expired.");
			}
			return values.GetEnumerator();
		}
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private void Wait() { _entered.TrySetResult(); if (!Release.Wait(TimeSpan.FromSeconds(5), ExecutionBudget.CurrentToken)) throw new TimeoutException(); }
	public IReadOnlyList<Diagnostic> Validate(string code, MushAnalysisMode mode = MushAnalysisMode.Function) { if (GateValidation && code == "slow[") Wait(); var values = FastValidation && code == "slow[" ? Array.Empty<Diagnostic>() : _inner.Validate(code, mode); return GateProjection && code == "slow[" ? new ProjectionList(values, this) : values; }
	public IReadOnlyList<CompletionSuggestion> Complete(string code, int line, int character) { if (!GateValidation && code == "get") Wait(); return _inner.Complete(code, line, character); }
	public string Format(string code) => _inner.Format(code);
	public string FormatIndented(string code, int width = 78, MushAnalysisMode mode = MushAnalysisMode.Function) => _inner.FormatIndented(code, width, mode);
	public HoverInfo? Hover(string code, int line, int character) => _inner.Hover(code, line, character);
	public SignatureInfo? SignatureHelp(string code, int line, int character) => _inner.SignatureHelp(code, line, character);
	public IReadOnlyList<CodeSymbol> DocumentSymbols(string code) => _inner.DocumentSymbols(code);
	public void Dispose() { Release.Set(); Release.Dispose(); }
}
