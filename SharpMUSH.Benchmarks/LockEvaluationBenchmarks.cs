using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks for the MUSH lock / boolean-expression subsystem.
/// Separates <em>compile</em> (lex → parse → LINQ expression tree → delegate)
/// from <em>evaluate</em> (call pre-compiled delegate) costs.
/// </summary>
[BenchmarkCategory("Lock/Boolean Expression")]
public class LockEvaluationBenchmarks : BaseBenchmark
{
	private IBooleanExpressionParser? _lockParser;
	private AnySharpObject? _godPlayer;

	// Pre-compiled locks — used for the evaluate-only benchmarks
	private Func<AnySharpObject, AnySharpObject, bool>? _simpleLock;
	private Func<AnySharpObject, AnySharpObject, bool>? _andLock;
	private Func<AnySharpObject, AnySharpObject, bool>? _orLock;
	private Func<AnySharpObject, AnySharpObject, bool>? _nestedLock;
	private Func<AnySharpObject, AnySharpObject, bool>? _complexLock;

	public LockEvaluationBenchmarks()
	{
		Setup().ConfigureAwait(false).GetAwaiter().GetResult();

		_lockParser = _server!.Services.GetRequiredService<IBooleanExpressionParser>();
		_godPlayer = _database!.GetObjectNodeAsync(new DBRef(1))
			.ConfigureAwait(false).GetAwaiter().GetResult()
			.Known;

		_simpleLock = _lockParser.Compile("#1");
		_andLock = _lockParser.Compile("#1&#1&#1");
		_orLock = _lockParser.Compile("#1|#2");
		_nestedLock = _lockParser.Compile("(#1|#2)&!(#2)");
		_complexLock = _lockParser.Compile("(#1|#2)&!(#3|#4)&(#1|#1)&!(#2|#2)");
	}

	// ── Compile benchmarks ────────────────────────────────────────────────────

	[Benchmark(Description = "Compile: #1 (trivial object match)")]
	public Func<AnySharpObject, AnySharpObject, bool> CompileSimple() =>
		_lockParser!.Compile("#1");

	[Benchmark(Description = "Compile: #1&#1&#1 (3-way AND)")]
	public Func<AnySharpObject, AnySharpObject, bool> CompileAndLock() =>
		_lockParser!.Compile("#1&#1&#1");

	[Benchmark(Description = "Compile: #1|#2 (OR)")]
	public Func<AnySharpObject, AnySharpObject, bool> CompileOrLock() =>
		_lockParser!.Compile("#1|#2");

	[Benchmark(Description = "Compile: (A|B)&!B (nested NOT)")]
	public Func<AnySharpObject, AnySharpObject, bool> CompileNestedLock() =>
		_lockParser!.Compile("(#1|#2)&!(#2)");

	[Benchmark(Description = "Compile: 8-term complex lock")]
	public Func<AnySharpObject, AnySharpObject, bool> CompileComplexLock() =>
		_lockParser!.Compile("(#1|#2)&!(#3|#4)&(#1|#1)&!(#2|#2)");

	// ── Evaluate benchmarks (pre-compiled delegate, no parsing overhead) ──────

	[Benchmark(Description = "Evaluate: simple #1 lock")]
	public bool EvaluateSimple() =>
		_simpleLock!(_godPlayer!, _godPlayer!);

	[Benchmark(Description = "Evaluate: AND lock (#1&#1&#1)")]
	public bool EvaluateAndLock() =>
		_andLock!(_godPlayer!, _godPlayer!);

	[Benchmark(Description = "Evaluate: OR lock (#1|#2)")]
	public bool EvaluateOrLock() =>
		_orLock!(_godPlayer!, _godPlayer!);

	[Benchmark(Description = "Evaluate: nested lock (A|B)&!B")]
	public bool EvaluateNestedLock() =>
		_nestedLock!(_godPlayer!, _godPlayer!);

	[Benchmark(Description = "Evaluate: complex 8-term lock")]
	public bool EvaluateComplexLock() =>
		_complexLock!(_godPlayer!, _godPlayer!);
}
