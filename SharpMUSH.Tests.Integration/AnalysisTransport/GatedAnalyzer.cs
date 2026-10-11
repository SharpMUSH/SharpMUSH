using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

internal sealed class GatedAnalyzer : IMushCodeAnalyzer, IDisposable
{
	private readonly IMushCodeAnalyzer _inner = AnalysisCatalogAnalyzer.Create(BuiltInAnalysisCatalog.CreateSnapshot(), SharpMUSHOptions.Default());
	public ManualResetEventSlim Entered { get; } = new();
	public ManualResetEventSlim Release { get; } = new();
	public bool GateValidation { get; init; }
	public bool GateProjection { get; init; }
	private sealed class ProjectionList(IReadOnlyList<Diagnostic> values, GatedAnalyzer owner) : IReadOnlyList<Diagnostic>
	{
		public int Count => values.Count;
		public Diagnostic this[int index] => values[index];
		public IEnumerator<Diagnostic> GetEnumerator()
		{
			owner.Entered.Set();
			if (!owner.Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
			return values.GetEnumerator();
		}
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private void Wait() { Entered.Set(); if (!Release.Wait(TimeSpan.FromSeconds(5), ExecutionBudget.CurrentToken)) throw new TimeoutException(); }
	public IReadOnlyList<Diagnostic> Validate(string code, MushAnalysisMode mode = MushAnalysisMode.Function) { if (GateValidation && code == "slow[") Wait(); var values = _inner.Validate(code, mode); return GateProjection && code == "slow[" ? new ProjectionList(values, this) : values; }
	public IReadOnlyList<CompletionSuggestion> Complete(string code, int line, int character) { if (!GateValidation && code == "get") Wait(); return _inner.Complete(code, line, character); }
	public string Format(string code) => _inner.Format(code);
	public string FormatIndented(string code, int width = 78, MushAnalysisMode mode = MushAnalysisMode.Function) => _inner.FormatIndented(code, width, mode);
	public HoverInfo? Hover(string code, int line, int character) => _inner.Hover(code, line, character);
	public SignatureInfo? SignatureHelp(string code, int line, int character) => _inner.SignatureHelp(code, line, character);
	public IReadOnlyList<CodeSymbol> DocumentSymbols(string code) => _inner.DocumentSymbols(code);
	public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
}
