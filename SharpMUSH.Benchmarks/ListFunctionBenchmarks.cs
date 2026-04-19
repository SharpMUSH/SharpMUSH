namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks for the built-in MUSH list and iteration function category.
/// Covers <c>lnum()</c>, <c>iter()</c>, <c>words()</c>, <c>member()</c>, <c>sort()</c>, and <c>map()</c>.
/// </summary>
[BenchmarkCategory("List Functions")]
public class ListFunctionBenchmarks : BaseBenchmark
{
	private IMUSHCodeParser? _parser;

	private static readonly MString Lnum10 = MModule.single("lnum(10)");
	private static readonly MString Lnum100 = MModule.single("lnum(100)");
	private static readonly MString Lnum1000 = MModule.single("lnum(1000)");

	private static readonly MString Iter10 = MModule.single("iter(lnum(10),%i0)");
	private static readonly MString Iter100 = MModule.single("iter(lnum(100),%i0)");
	private static readonly MString Iter1000 = MModule.single("iter(lnum(1000),%i0)");

	private static readonly MString Words10 = MModule.single(
		$"words({string.Join(" ", Enumerable.Range(1, 10))})");
	private static readonly MString Words100 = MModule.single(
		$"words({string.Join(" ", Enumerable.Range(1, 100))})");

	private static readonly MString Member10 = MModule.single(
		$"member({string.Join(" ", Enumerable.Range(1, 10))},5)");
	private static readonly MString Member100 = MModule.single(
		$"member({string.Join(" ", Enumerable.Range(1, 100))},50)");

	// Reversed order to stress sort
	private static readonly MString Sort10 = MModule.single(
		$"sort({string.Join(" ", Enumerable.Range(1, 10).Select(i => (11 - i).ToString()))})");
	private static readonly MString Sort100 = MModule.single(
		$"sort({string.Join(" ", Enumerable.Range(1, 100).Select(i => (101 - i).ToString()))})");

	private static readonly MString MapInput10 = MModule.single("map(upcase,lnum(10))");
	private static readonly MString MapInput100 = MModule.single("map(upcase,lnum(100))");

	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);
		_parser = await TestParser().ConfigureAwait(false);
	}

	[Benchmark(Description = "lnum(N) — generate N-element list")]
	[Arguments(10)]
	[Arguments(100)]
	[Arguments(1000)]
	public async Task Lnum(int count)
	{
		var input = count switch { 10 => Lnum10, 100 => Lnum100, _ => Lnum1000 };
		await _parser!.FunctionParse(input);
	}

	[Benchmark(Description = "iter(lnum(N),%i0) — iterate N items")]
	[Arguments(10)]
	[Arguments(100)]
	[Arguments(1000)]
	public async Task Iter(int count)
	{
		var input = count switch { 10 => Iter10, 100 => Iter100, _ => Iter1000 };
		await _parser!.FunctionParse(input);
	}

	[Benchmark(Description = "words(N-element list)")]
	[Arguments(10)]
	[Arguments(100)]
	public async Task Words(int count)
	{
		var input = count == 10 ? Words10 : Words100;
		await _parser!.FunctionParse(input);
	}

	[Benchmark(Description = "member(list,value) — linear scan")]
	[Arguments(10)]
	[Arguments(100)]
	public async Task Member(int count)
	{
		var input = count == 10 ? Member10 : Member100;
		await _parser!.FunctionParse(input);
	}

	[Benchmark(Description = "sort(reversed-list) — stress sort")]
	[Arguments(10)]
	[Arguments(100)]
	public async Task Sort(int count)
	{
		var input = count == 10 ? Sort10 : Sort100;
		await _parser!.FunctionParse(input);
	}

	[Benchmark(Description = "map(upcase,lnum(N)) — function applied per element")]
	[Arguments(10)]
	[Arguments(100)]
	public async Task Map(int count)
	{
		var input = count == 10 ? MapInput10 : MapInput100;
		await _parser!.FunctionParse(input);
	}
}
