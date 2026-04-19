namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks for the built-in MUSH string function category.
/// Exercises <see cref="IMUSHCodeParser.FunctionParse(MString)"/> with common string operations.
/// </summary>
[BenchmarkCategory("String Functions")]
public class StringFunctionBenchmarks : BaseBenchmark
{
	private IMUSHCodeParser? _parser;

	// Fixed inputs
	private static readonly MString MidInput = MModule.single("mid(abcdefghij,2,5)");
	private static readonly MString LjustInput = MModule.single("ljust(hello,20)");
	private static readonly MString RjustInput = MModule.single("rjust(hello,20)");
	private static readonly MString TrimInput = MModule.single("trim(  hello world  )");
	private static readonly MString CenterInput = MModule.single("center(hi,20)");

	// Parameterized by length — pre-computed to avoid per-iteration string allocation
	private static readonly MString Left10 = MModule.single($"left({new string('x', 10)},5)");
	private static readonly MString Left100 = MModule.single($"left({new string('x', 100)},5)");
	private static readonly MString Left1000 = MModule.single($"left({new string('x', 1000)},5)");

	private static readonly MString Strlen10 = MModule.single($"strlen({new string('x', 10)})");
	private static readonly MString Strlen100 = MModule.single($"strlen({new string('x', 100)})");
	private static readonly MString Strlen1000 = MModule.single($"strlen({new string('x', 1000)})");

	private static readonly MString Cat5 = MModule.single("cat(a,b,c,d,e)");
	private static readonly MString Cat26 = MModule.single(
		$"cat({string.Join(",", Enumerable.Range('a', 26).Select(c => ((char)c).ToString()))})");

	[GlobalSetup]
	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);
		_parser = await TestParser().ConfigureAwait(false);
	}

	[Benchmark(Description = "mid(abcdefghij,2,5)")]
	public async Task Mid() =>
		await _parser!.FunctionParse(MidInput);

	[Benchmark(Description = "left(Nx,5) — vary input length")]
	[Arguments(10)]
	[Arguments(100)]
	[Arguments(1000)]
	public async Task Left(int length)
	{
		var input = length switch { 10 => Left10, 100 => Left100, _ => Left1000 };
		await _parser!.FunctionParse(input);
	}

	[Benchmark(Description = "strlen(Nx) — vary input length")]
	[Arguments(10)]
	[Arguments(100)]
	[Arguments(1000)]
	public async Task Strlen(int length)
	{
		var input = length switch { 10 => Strlen10, 100 => Strlen100, _ => Strlen1000 };
		await _parser!.FunctionParse(input);
	}

	[Benchmark(Description = "ljust(hello,20)")]
	public async Task Ljust() =>
		await _parser!.FunctionParse(LjustInput);

	[Benchmark(Description = "rjust(hello,20)")]
	public async Task Rjust() =>
		await _parser!.FunctionParse(RjustInput);

	[Benchmark(Description = "center(hi,20)")]
	public async Task Center() =>
		await _parser!.FunctionParse(CenterInput);

	[Benchmark(Description = "trim(  hello world  )")]
	public async Task Trim() =>
		await _parser!.FunctionParse(TrimInput);

	[Benchmark(Description = "cat(N args) — vary arg count")]
	[Arguments(5)]
	[Arguments(26)]
	public async Task Cat(int count)
	{
		var input = count == 5 ? Cat5 : Cat26;
		await _parser!.FunctionParse(input);
	}
}
