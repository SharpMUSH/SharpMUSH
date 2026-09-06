namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks for MUSH command dispatch via <see cref="IMUSHCodeParser.CommandParse(MString)"/>.
/// Covers the hot path from raw input → ANTLR4 parse → command lookup (trie) → command execution.
/// </summary>
[BenchmarkCategory("Command Dispatch")]
public class CommandParseBenchmarks : BaseBenchmark
{
	private static readonly MString ThinkSimpleInput = MModule.single("think Hello World");
	private static readonly MString ThinkSubstInput = MModule.single("think %#");
	private static readonly MString ThinkNameSubstInput = MModule.single("think %N");
	private static readonly MString PemitSelfInput = MModule.single("@pemit me=Hello World");
	private static readonly MString SetAttrInput = MModule.single("@set me=SAFE");
	private static readonly MString PemitWithFunctionInput = MModule.single("@pemit me=[add(1,2)]");


	[Benchmark(Description = "think with literal text")]
	public async Task ThinkSimple() =>
		await FreshParser().CommandParse(ThinkSimpleInput);

	[Benchmark(Description = "think with %# (executor dbref)")]
	public async Task ThinkWithDbRefSubstitution() =>
		await FreshParser().CommandParse(ThinkSubstInput);

	[Benchmark(Description = "think with %N (executor name)")]
	public async Task ThinkWithNameSubstitution() =>
		await FreshParser().CommandParse(ThinkNameSubstInput);

	[Benchmark(Description = "@pemit me=Hello World")]
	public async Task PemitSelf() =>
		await FreshParser().CommandParse(PemitSelfInput);

	[Benchmark(Description = "@pemit me=[add(1,2)] (function call in command argument)")]
	public async Task PemitWithFunctionInArgument() =>
		await FreshParser().CommandParse(PemitWithFunctionInput);

	[Benchmark(Description = "@set me=SAFE (flag toggle)")]
	public async Task SetFlag() =>
		await FreshParser().CommandParse(SetAttrInput);
}
